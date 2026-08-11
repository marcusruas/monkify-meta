namespace SpaceStroker.WalletFunder;

public sealed record FunderOptions(
    string ReceiptsPath,
    string? PoolPath,
    bool Execute,
    bool StatusOnly,
    bool ForcePlanChange,
    int? SpreadMinutes);

public sealed class Funder(SecretsFile secrets, FunderOptions options)
{
    /// <summary>0 = success (all confirmed, dry run, or empty pool) · 1 = anything unresolved.</summary>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var pool = options.PoolPath is null
            ? WalletPool.FromEntries(secrets.Wallets)
            : WalletPool.Parse(await File.ReadAllTextAsync(options.PoolPath, ct));

        if (pool.Count == 0)
        {
            Console.WriteLine("The wallet pool is empty — nothing to do.");
            return 0;
        }

        var plan = FundingPlan.Build(pool);
        var receipts = Receipts.LoadOrCreate(options.ReceiptsPath, plan, options.ForcePlanChange);

        using var coinbase = new CoinbaseClient(secrets.Coinbase);
        var confirmer = new SolanaConfirmer(secrets.Solana);

        var account = await coinbase.ResolveSolAccountAsync(secrets.Coinbase.AccountId, ct);

        // `status` is read-only. It cannot send — that is the whole point of having it.
        if (options.StatusOnly)
        {
            foreach (var transfer in receipts.Outstanding(plan))
            {
                var pending = receipts.For(transfer);
                if (string.IsNullOrWhiteSpace(pending.CoinbaseTransactionId)) continue;

                await SettleAsync(coinbase, confirmer, account, transfer, pending, receipts, snapshotLamports: 0, ct);
            }

            ConsoleReport.PrintSummary(receipts);
            return receipts.AllConfirmed ? 0 : 1;
        }

        var preflight = Preflight.Check(
            pool, account.Balance, secrets.Funding.MinAmountSol, secrets.Funding.NetworkFeeHeadroomSol);

        if (!preflight.Ok)
        {
            foreach (var error in preflight.Errors) Console.Error.WriteLine($"preflight: {error}");
            throw new FunderException($"Preflight failed with {preflight.Errors.Count} problem(s) — nothing was sent.");
        }

        // Snapshot every destination BEFORE sending; confirmation is a delta against this.
        var snapshots = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var transfer in plan.Transfers)
            snapshots[transfer.Address] = await confirmer.GetBalanceLamportsAsync(transfer.Address, ct);

        var window = TimeSpan.FromMinutes(options.SpreadMinutes ?? secrets.Funding.SpreadMinutes);
        ConsoleReport.PrintPlan(plan, receipts, account.Balance, secrets.Funding.NetworkFeeHeadroomSol,
            window, options.Execute);

        if (!options.Execute)
        {
            Console.WriteLine("Dry run — nothing was sent. Re-run with --execute to fund the pool.");
            return 0;
        }

        if (!ConsoleReport.ConfirmTotal(plan.TotalSol, plan.Transfers.Count, Console.In))
        {
            Console.Error.WriteLine("error: confirmation did not match the batch total — nothing was sent.");
            return 1;
        }

        var schedule = SendSchedule.Build(receipts.Outstanding(plan), window, Random.Shared);
        var started = DateTimeOffset.UtcNow;

        foreach (var scheduled in schedule)
        {
            var delay = scheduled.Offset - (DateTimeOffset.UtcNow - started);
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);

            var entry = receipts.For(scheduled.Transfer);

            if (Receipts.NeedsSend(entry))
            {
                try
                {
                    var sent = await coinbase.SendAsync(account.Id, scheduled.Transfer.Address,
                        scheduled.Transfer.SolAmount, scheduled.Transfer.Idem, ct);

                    // Persist the transaction id IMMEDIATELY — a crash here must never lose it.
                    receipts.Update(entry, e =>
                    {
                        e.CoinbaseTransactionId = sent.TransactionId;
                        e.Status = TransferStatus.Sent;
                        e.SentUtc = DateTimeOffset.UtcNow;
                        e.Error = null;
                    });

                    Console.WriteLine($"{scheduled.Transfer.Label}: sent " +
                                      $"{WalletPool.FormatSol(scheduled.Transfer.SolAmount)} SOL ({sent.TransactionId}).");
                }
                catch (CoinbaseAuthException)
                {
                    throw;      // configuration problem — fatal for the whole run
                }
                catch (FunderException ex)
                {
                    receipts.Update(entry, e =>
                    {
                        e.Status = TransferStatus.Failed;
                        e.Error = ex.Message;
                    });

                    Console.Error.WriteLine($"{scheduled.Transfer.Label}: FAILED — {ex.Message}");
                    continue;   // one wallet's failure never stops the batch
                }
            }

            await SettleAsync(coinbase, confirmer, account, scheduled.Transfer, entry, receipts,
                snapshots.GetValueOrDefault(scheduled.Transfer.Address, 0), ct);
        }

        ConsoleReport.PrintSummary(receipts);
        return receipts.AllConfirmed ? 0 : 1;
    }

    /// <summary>Poll Coinbase to a terminal status, then require an on-chain credit before "Confirmed".</summary>
    private async Task SettleAsync(
        CoinbaseClient coinbase, SolanaConfirmer confirmer, CoinbaseAccount account, PlannedTransfer transfer,
        ReceiptEntry entry, Receipts receipts, long snapshotLamports, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entry.CoinbaseTransactionId)) return;

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(secrets.Funding.SendPollTimeoutSeconds);
        var status = "";

        while (DateTimeOffset.UtcNow < deadline)
        {
            status = await coinbase.GetTransactionStatusAsync(account.Id, entry.CoinbaseTransactionId!, ct);
            if (status is "completed" or "failed" or "canceled" or "expired") break;

            receipts.Update(entry, e => e.Status = TransferStatus.Pending);
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }

        if (status is "failed" or "canceled" or "expired")
        {
            receipts.Update(entry, e =>
            {
                e.Status = TransferStatus.Failed;
                e.Error = $"Coinbase reported '{status}'.";
            });

            Console.Error.WriteLine($"{transfer.Label}: Coinbase reported '{status}'.");
            return;
        }

        if (status != "completed")
        {
            receipts.Update(entry, e =>
            {
                e.Status = TransferStatus.Pending;
                e.Error = "Still pending when the run ended.";
            });

            Console.Error.WriteLine(
                $"{transfer.Label}: still pending — re-run to resume polling (it will not be re-sent).");
            return;
        }

        var credited = await confirmer.WaitForCreditAsync(transfer.Address, snapshotLamports, transfer.Lamports,
            TimeSpan.FromSeconds(secrets.Funding.ChainConfirmTimeoutSeconds), ct);

        receipts.Update(entry, e =>
        {
            e.ChainConfirmed = credited;
            e.Status = credited ? TransferStatus.Confirmed : TransferStatus.Unconfirmed;
            e.SettledUtc = DateTimeOffset.UtcNow;
            e.Error = credited ? null : "Coinbase reported completed but no on-chain credit was observed.";
        });

        Console.WriteLine(credited
            ? $"{transfer.Label}: confirmed on-chain."
            : $"{transfer.Label}: UNCONFIRMED — Coinbase says completed but the balance did not rise.");
    }
}
