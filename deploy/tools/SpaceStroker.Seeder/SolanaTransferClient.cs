using Solnet.Programs;
using Solnet.Rpc;
using Solnet.Rpc.Builders;
using Solnet.Rpc.Types;
using Solnet.Wallet;

namespace SpaceStroker.Seeder;

public sealed record TransferResult(bool Ok, string? Signature, string Error);

/// <summary>
/// The on-chain half, driven through the API's own POST /rpc proxy — so this tool holds NO RPC key.
///
/// The proxy allow-lists exactly three methods: getLatestBlockhash, sendTransaction and
/// getSignatureStatuses. Everything here is built from those three; do not add a call that needs
/// another one (the allow-list was deliberately narrowed 7 -> 3 by frontend-helius-gateway and must
/// not be widened for this tool). The proxy also rejects JSON-RPC BATCH arrays, so only
/// single-request Solnet calls are safe.
/// </summary>
public sealed class SolanaTransferClient
{
    private readonly IRpcClient _rpc;

    public SolanaTransferClient(string rpcUrl) => _rpc = ClientFactory.GetClient(rpcUrl);

    /// <summary>
    /// Exact integer conversion. Deliberately NOT Math.Pow on a double — that routes a money value
    /// through binary floating point, where a 1e-9 error becomes a real token amount.
    /// </summary>
    public static ulong ToRawAmount(decimal uiAmount, int decimals)
    {
        if (uiAmount < 0)
            throw new SeederException($"A negative stake ({uiAmount}) can never be transferred.");

        var scaled = uiAmount;
        for (var i = 0; i < decimals; i++)
            scaled *= 10m;

        if (scaled != decimal.Truncate(scaled))
            throw new SeederException(
                $"Stake {uiAmount} needs more than {decimals} decimal places — it cannot be sent exactly.");

        return (ulong)scaled;
    }

    /// <summary>
    /// One TransferChecked from the seeder's associated token account to the operator's.
    ///
    /// Both ATAs are DERIVED LOCALLY (pure PDA maths, zero RPC) and NO create-ATA instruction is
    /// added: the destination already receives every player's bet, and the source must already hold
    /// the token or the balance pre-flight would have skipped this game. Solnet 6.1.0 has no
    /// idempotent create, so adding a non-idempotent one would fail on an account that exists.
    ///
    /// The destination is derived from the token OWNER because that is what the backend validates:
    /// SolanaService.ValidateBetPayment matches the transaction's pre/post token balances by owner,
    /// not by a specific account.
    /// </summary>
    public async Task<TransferResult> SendStakeAsync(
        Account payer,
        string mintAddress,
        string receiverOwner,
        decimal uiAmount,
        int decimals,
        CancellationToken ct)
    {
        var mint = new PublicKey(mintAddress);
        var source = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(payer.PublicKey, mint);
        var destination = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(new PublicKey(receiverOwner), mint);

        if (source is null || destination is null)
            return new(false, null, "could not derive the associated token accounts for this mint");

        var raw = ToRawAmount(uiAmount, decimals);

        var blockhash = await _rpc.GetLatestBlockHashAsync(Commitment.Confirmed);
        if (!blockhash.WasSuccessful || string.IsNullOrWhiteSpace(blockhash.Result?.Value?.Blockhash))
            return new(false, null, $"could not read a recent blockhash ({Describe(blockhash.Reason, blockhash.HttpStatusCode)})");

        var transaction = new TransactionBuilder()
            .SetRecentBlockHash(blockhash.Result.Value.Blockhash)
            .SetFeePayer(payer)
            .AddInstruction(TokenProgram.TransferChecked(source, destination, raw, decimals, payer.PublicKey, mint))
            .Build(payer);

        ct.ThrowIfCancellationRequested();

        // preflight commitment MUST be Confirmed to match the blockhash read above. Solnet defaults
        // it to Finalized, which cannot see a confirmed-but-not-finalized blockhash, and every send
        // then fails with "blockhash not found". specs/solana-connection-rpc.md hit exactly this on
        // the frontend.
        var sent = await _rpc.SendTransactionAsync(transaction, skipPreflight: false, commitment: Commitment.Confirmed);

        if (!sent.WasSuccessful || string.IsNullOrWhiteSpace(sent.Result))
        {
            return new(false, null,
                $"send failed ({Describe(sent.Reason, sent.HttpStatusCode)}) "
              + "— the seeder wallet may be out of SOL for transaction fees");
        }

        return new(true, sent.Result, "");
    }

    /// <summary>
    /// Polls getSignatureStatuses until the signature confirms or the timeout elapses.
    ///
    /// This — and the bet POST — are the ONLY things that may repeat once a signature exists. The
    /// transfer itself is never re-sent.
    /// </summary>
    public async Task<bool> ConfirmAsync(string signature, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var statuses = await _rpc.GetSignatureStatusesAsync(new List<string> { signature }, searchTransactionHistory: true);
            var info = statuses.WasSuccessful ? statuses.Result?.Value?.FirstOrDefault() : null;

            if (info?.Error is not null)
            {
                Log.Error($"the stake transfer {Short(signature)} failed on chain");
                return false;
            }

            if (info?.ConfirmationStatus is "confirmed" or "finalized")
                return true;

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        return false;
    }

    private static string Describe(string? reason, System.Net.HttpStatusCode status)
        => string.IsNullOrWhiteSpace(reason) ? $"HTTP {(int)status}" : reason;

    /// <summary>Shortens a signature for routine log lines. Orphan logs print it in FULL.</summary>
    public static string Short(string signature)
        => signature.Length <= 16 ? signature : $"{signature[..8]}…{signature[^4..]}";
}
