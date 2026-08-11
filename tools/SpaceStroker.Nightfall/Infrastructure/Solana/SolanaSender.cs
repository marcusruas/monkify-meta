using System.Text.Json;
using Solnet.Rpc;
using Solnet.Rpc.Types;
using SpaceStroker.Nightfall.Domain;

namespace SpaceStroker.Nightfall.Infrastructure.Solana;

/// <summary>
/// Broadcasting and settlement. Every byte sent here was signed locally by
/// <see cref="TransactionSigner"/> after <see cref="SwapInstructionsGuard"/> approved the swap.
/// </summary>
public sealed class SolanaSender(IRpcClient rpc)
{
    public async Task<string> SendAsync(byte[] signedTransaction, CancellationToken ct)
    {
        // preflightCommitment MUST match the commitment the blockhash was fetched at, or the node
        // rejects a blockhash it has not seen yet — the lesson specs/solana-connection-rpc.md
        // recorded on the frontend's sendToken.
        var result = await rpc.SendTransactionAsync(signedTransaction, skipPreflight: false, Commitment.Confirmed);

        if (result.WasSuccessful && !string.IsNullOrWhiteSpace(result.Result))
            return result.Result;

        var reason = result.Reason ?? result.RawRpcResponse ?? "unknown error";
        throw new NightfallException($"The transaction was rejected: {reason}");
    }

    /// <summary>
    /// Polls to `confirmed`. A timeout is deliberately RETRYABLE, not terminal: the transaction may
    /// still land, so the caller keeps the signature and reports Unconfirmed rather than Failed.
    /// </summary>
    public async Task WaitForConfirmationAsync(string signature, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = await rpc.GetSignatureStatusesAsync([signature], searchTransactionHistory: true);
            var status = result.WasSuccessful ? result.Result?.Value?.FirstOrDefault() : null;

            if (status is not null)
            {
                if (status.Error is not null)
                    throw new NightfallException($"The transaction failed on chain: {status.Error.Type}");

                if (status.ConfirmationStatus is "confirmed" or "finalized")
                    return;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        throw new NightfallException(
            $"The transaction was not confirmed in {timeout.TotalSeconds:0} seconds. It may still land — check the signature.");
    }

    /// <summary>
    /// What the sale actually paid, from the confirmed transaction's own balance deltas.
    ///
    /// Reads the RAW rpc JSON rather than a convenience conversion, the same way
    /// SolanaService.GetAccountBalances does — an unparseable field must be an error, never a
    /// silently-zero payout.
    /// </summary>
    public async Task<long> GetRealizedLamportsAsync(string signature, string wallet, CancellationToken ct)
    {
        var result = await rpc.GetTransactionAsync(signature, Commitment.Confirmed);

        if (!result.WasSuccessful || string.IsNullOrWhiteSpace(result.RawRpcResponse))
            throw new NightfallException($"Could not read the confirmed transaction {signature}.");

        using var document = JsonDocument.Parse(result.RawRpcResponse);

        if (!document.RootElement.TryGetProperty("result", out var root) || root.ValueKind == JsonValueKind.Null)
            throw new NightfallException($"The RPC has no record of transaction {signature} yet.");

        if (!root.TryGetProperty("meta", out var meta))
            throw new NightfallException($"Transaction {signature} carried no metadata.");

        var pre = ReadLongs(meta, "preBalances");
        var post = ReadLongs(meta, "postBalances");
        var fee = meta.TryGetProperty("fee", out var feeElement) ? feeElement.GetInt64() : 0L;

        var index = IndexOfAccount(root, wallet);

        return RealizedSol.From(pre, post, fee, index);
    }

    private static long[] ReadLongs(JsonElement meta, string name)
        => meta.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(e => e.GetInt64()).ToArray()
            : throw new NightfallException($"The confirmed transaction carried no {name}.");

    /// <summary>
    /// The wallet is the fee payer, so it is account 0 in practice — but the index is resolved from
    /// the account keys rather than assumed, because reading the wrong account's delta would report
    /// a confident and completely wrong payout.
    /// </summary>
    private static int IndexOfAccount(JsonElement root, string wallet)
    {
        if (root.TryGetProperty("transaction", out var transaction) &&
            transaction.TryGetProperty("message", out var message) &&
            message.TryGetProperty("accountKeys", out var keys) &&
            keys.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var key in keys.EnumerateArray())
            {
                var value = key.ValueKind == JsonValueKind.Object && key.TryGetProperty("pubkey", out var pk)
                    ? pk.GetString()
                    : key.GetString();

                if (value == wallet) return index;
                index++;
            }
        }

        throw new NightfallException("The confirmed transaction does not list this wallet among its accounts.");
    }
}
