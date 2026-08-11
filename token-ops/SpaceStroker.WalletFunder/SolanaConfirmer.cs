using Solnet.Rpc;
using Solnet.Rpc.Types;

namespace SpaceStroker.WalletFunder;

/// <summary>
/// Independent proof the SOL actually landed. Coinbase reporting `completed` is Coinbase's view of a
/// withdrawal it has broadcast; card #63 cannot snipe with SOL that has not arrived.
/// </summary>
public sealed class SolanaConfirmer(SolanaSection solana)
{
    private readonly IRpcClient _rpc = ClientFactory.GetClient(solana.RpcUrl);

    public async Task<long> GetBalanceLamportsAsync(string address, CancellationToken ct)
    {
        var result = await _rpc.GetBalanceAsync(address, Commitment.Confirmed);
        if (!result.WasSuccessful)
            throw new FunderException($"Solana RPC could not read the balance of {address}: {result.Reason}");

        return (long)result.Result.Value;
    }

    /// <summary>
    /// Waits for a DELTA against the preflight snapshot, not an absolute balance: the wallet may already
    /// hold SOL, and ">=" tolerates an unrelated concurrent deposit.
    /// </summary>
    public async Task<bool> WaitForCreditAsync(
        string address, long snapshotLamports, long expectedLamports, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                if (await GetBalanceLamportsAsync(address, ct) - snapshotLamports >= expectedLamports) return true;
            }
            catch (FunderException)
            {
                // Transient RPC trouble — keep polling until the deadline rather than failing the wallet.
            }

            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }

        return false;
    }
}
