using Solnet.Wallet;
using SpaceStroker.Nightfall.Domain;
using SpaceStroker.Nightfall.Infrastructure;
using SpaceStroker.Nightfall.Infrastructure.Jupiter;
using SpaceStroker.Nightfall.Infrastructure.Solana;

namespace SpaceStroker.Nightfall.Application;

public sealed record SellEvent(string PublicKey, string Alias, string Status, string? Message, string? Signature,
    decimal? QuotedSol, decimal? RealizedSol);

/// <summary>
/// One batch. Every selected wallet runs independently, capped at maxConcurrentSells — one wallet's
/// failure never cancels another's sale.
/// </summary>
public sealed class SellRunner(
    NightfallState state,
    SecretsFile secrets,
    JupiterClient jupiter,
    SolanaReader reader,
    SolanaSender sender,
    TokenAccountCloser closer,
    ReceiptsStore receipts,
    Action<SellEvent> publish,
    CancellationToken applicationStopping)
{
    /// <summary>
    /// Starts the batch and returns its id immediately; the browser follows progress over SSE rather
    /// than holding a request open for the length of a sale.
    /// </summary>
    /// <remarks>
    /// ⚠️ The batch deliberately does NOT run under the request's CancellationToken. That token is
    /// <c>HttpContext.RequestAborted</c>, which fires the moment this response completes — so a
    /// fire-and-forget batch started with it is cancelled instantly and silently. (A live smoke test
    /// caught exactly that: the endpoint returned a batch id and nothing ever happened.) The batch
    /// belongs to the application's lifetime, not to the HTTP call that asked for it.
    /// </remarks>
    public Task<string> RunAsync(IReadOnlyList<string> wallets, decimal? percent, CancellationToken requestAborted)
    {
        var batchId = Guid.NewGuid().ToString("N")[..8];

        // Named and then deliberately unused, so the next reader sees the choice rather than an
        // omission. See the remarks above.
        _ = requestAborted;

        var ct = applicationStopping;

        _ = Task.Run(async () =>
        {
            // Disposed only after every wallet has finished — disposing it when RunAsync returns
            // would pull the semaphore out from under the batch still using it.
            using var gate = new SemaphoreSlim(secrets.Sell.MaxConcurrentSells);

            var work = wallets.Select(async publicKey =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    await SellOneAsync(publicKey, percent, batchId, ct);
                }
                finally
                {
                    gate.Release();
                }
            });

            await Task.WhenAll(work);
        }, ct);

        return Task.FromResult(batchId);
    }

    private async Task SellOneAsync(string publicKey, decimal? percent, string batchId, CancellationToken ct)
    {
        var wallet = state.Find(publicKey);

        if (wallet is null)
        {
            Publish(publicKey, publicKey, "Failed", "This wallet is not in the pool.");
            return;
        }

        // Claimed here and released in the finally — a wallet already selling is never submitted
        // twice, however many times the browser fires.
        if (!state.TryBeginSell(publicKey))
        {
            Publish(publicKey, wallet.Alias, "Failed", "This wallet is already selling.");
            return;
        }

        var receipt = new SellReceipt
        {
            Alias = wallet.Alias,
            PublicKey = publicKey,
            BatchId = batchId,
            Percent = percent,
        };

        try
        {
            receipts.Record(receipt);
            state.Record(receipt);

            await ExecuteAsync(wallet, receipt, percent, ct);
        }
        catch (Exception ex)
        {
            Fail(receipt, ex.Message);
        }
        finally
        {
            state.EndSell(publicKey);
            await RefreshAsync(publicKey, ct);
        }
    }

    private async Task ExecuteAsync(PoolEntry wallet, SellReceipt receipt, decimal? percent, CancellationToken ct)
    {
        // 1. A FRESH balance read — never the polled figure, which is up to refreshIntervalSeconds
        //    stale. "Sell 100%" has to mean 100% of what is actually there right now.
        var tokenRaw = await reader.GetTokenBalanceAsync(wallet.PublicKey, ct);
        var solLamports = state.BalanceOf(wallet.PublicKey).SolLamports;

        var exclusion = SellPreflight.Check(tokenRaw, solLamports, secrets.Sell.MinSolForFeeSol);
        if (exclusion != SellExclusion.None)
        {
            Fail(receipt, SellPreflight.Describe(exclusion));
            return;
        }

        var amount = SellPlan.AmountFor(tokenRaw, percent);
        receipt.TokenAmountRaw = amount.ToString();
        receipt.TokenAmount = TokenAmount.ToUi(amount, state.Mint.Decimals);

        // 2. Quote.
        var quote = await jupiter.QuoteAsync(secrets.Token.Mint, amount, ct);
        receipt.MoveTo(SellStatus.Quoted);
        receipt.QuotedSol = quote.OutSol;
        Save(receipt);
        Publish(receipt, "Quoted");

        // 3. Price impact — the abort happens BEFORE anything is built or broadcast.
        var impact = SellPreflight.CheckPriceImpact(quote.PriceImpactPercent, secrets.Jupiter.MaxPriceImpactPercent);
        if (impact != SellExclusion.None)
        {
            Fail(receipt, $"{SellPreflight.Describe(impact)} (impact {TokenAmount.Format(quote.PriceImpactPercent)}%, " +
                          $"max {TokenAmount.Format(secrets.Jupiter.MaxPriceImpactPercent)}%)");
            return;
        }

        // 4. SIMULATE stops here — and the check is SERVER-SIDE, so a browser that posts a sell
        //    while the host is simulating still sells nothing.
        if (!state.IsLive)
        {
            receipt.MoveTo(SellStatus.Simulated);
            Save(receipt);
            Publish(receipt, "Simulated", $"Would sell {TokenAmount.Format(receipt.TokenAmount)} for " +
                                          $"~{TokenAmount.Format(quote.OutSol)} SOL. Nothing was broadcast.");
            return;
        }

        // 5. Vet the swap before a signature exists.
        var instructions = await jupiter.SwapInstructionsAsync(quote, wallet.PublicKey, ct);
        SwapInstructionsGuard.Assert(
            instructions, wallet.PublicKey, secrets.Token.Mint,
            quote.InputMint, quote.OutputMint, amount, quote.InAmount);

        // 6. Sign locally. The key never leaves this process.
        var wire = await jupiter.SwapTransactionAsync(quote, wallet.PublicKey, ct);
        var signed = TransactionSigner.Sign(wire, new Account(wallet.PrivateKey, wallet.PublicKey));

        // 7. Broadcast through OUR rpc.
        var signature = await sender.SendAsync(signed, ct);
        receipt.MoveTo(SellStatus.Sent);
        receipt.Signature = signature;
        Save(receipt);
        Publish(receipt, "Sent");

        try
        {
            await sender.WaitForConfirmationAsync(signature, TimeSpan.FromSeconds(secrets.Sell.ConfirmTimeoutSeconds), ct);
        }
        catch (NightfallException ex) when (SellError.Classify(null, ex.Message, false) == SellErrorClass.Retryable)
        {
            // The transaction may still land, so this is Unconfirmed — NOT Failed. The signature is
            // kept so the operator can check it rather than assume the money is gone.
            receipt.MoveTo(SellStatus.Unconfirmed);
            receipt.Error = ex.Message;
            Save(receipt);
            Publish(receipt, "Unconfirmed", ex.Message);
            return;
        }

        // 8. What it actually paid.
        receipt.MoveTo(SellStatus.Sold);
        receipt.RealizedSol = TokenAmount.LamportsToSol(
            await sender.GetRealizedLamportsAsync(signature, wallet.PublicKey, ct));
        Save(receipt);
        Publish(receipt, "Sold");

        // 9. Reclaim the rent — separate transaction, and a failure here never touches the sale.
        var soldEverything = percent is null || percent == 100m;

        if (soldEverything && secrets.Sell.CloseEmptiedTokenAccounts)
        {
            receipt.CloseSignature = await closer.TryCloseAsync(wallet, ct);
            Save(receipt);
        }
    }

    private async Task RefreshAsync(string publicKey, CancellationToken ct)
    {
        try
        {
            var wallet = state.Find(publicKey);
            if (wallet is null) return;

            var balances = await reader.GetBalancesAsync([wallet], ct);

            if (balances.TryGetValue(publicKey, out var balance))
                state.ApplyBalance(publicKey, balance);
        }
        catch (Exception)
        {
            // A refresh that fails is cosmetic — the next poll picks it up.
        }
    }

    private void Fail(SellReceipt receipt, string message)
    {
        if (receipt.Status is not (SellStatus.Sold or SellStatus.Failed))
            receipt.MoveTo(SellStatus.Failed);

        receipt.Error = message;
        Save(receipt);
        Publish(receipt, "Failed", message);
    }

    private void Save(SellReceipt receipt)
    {
        receipts.Record(receipt);
        state.Record(receipt);
    }

    private void Publish(SellReceipt receipt, string status, string? message = null)
        => publish(new SellEvent(receipt.PublicKey, receipt.Alias, status, message ?? receipt.Error,
            receipt.Signature, receipt.QuotedSol, receipt.RealizedSol));

    private void Publish(string publicKey, string alias, string status, string message)
        => publish(new SellEvent(publicKey, alias, status, message, null, null, null));
}
