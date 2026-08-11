using System.Globalization;
using System.Text.Json;
using Solnet.Programs;
using Solnet.Rpc;
using Solnet.Rpc.Types;
using Solnet.Wallet;
using SpaceStroker.Nightfall.Domain;

namespace SpaceStroker.Nightfall.Infrastructure.Solana;

public sealed record WalletBalances(long SolLamports, ulong TokenRaw);

public sealed record MintInfo(ulong Supply, int Decimals);

/// <summary>
/// Every read the dashboard makes, batched so a 20-wallet pool costs two RPC calls per refresh
/// rather than forty.
/// </summary>
public sealed class SolanaReader(IRpcClient rpc, string mint)
{
    public async Task<MintInfo> GetMintAsync(CancellationToken ct)
    {
        var result = await rpc.GetTokenSupplyAsync(mint, Commitment.Confirmed);

        if (!result.WasSuccessful || result.Result?.Value is null)
            throw new NightfallException(
                $"Could not read the supply of {mint}. The token mint or the RPC endpoint may be wrong. ({result.Reason})");

        var supply = ulong.TryParse(result.Result.Value.Amount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new NightfallException($"The RPC returned an unreadable supply for {mint}.");

        return new MintInfo(supply, result.Result.Value.Decimals);
    }

    /// <summary>
    /// Two calls for the whole pool: one getMultipleAccounts over the wallets for lamports, one over
    /// their derived associated token accounts for token balances.
    ///
    /// ⚠️ A missing account is a ZERO balance, not an error — a wallet that has never held the token
    /// simply has no ATA. Both TokenBalanceFailed and SolBalanceFailed in monkify-backend make the
    /// same distinction, and getting it wrong makes every fresh wallet look broken.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, WalletBalances>> GetBalancesAsync(
        IReadOnlyList<PoolEntry> pool, CancellationToken ct)
    {
        var result = new Dictionary<string, WalletBalances>(pool.Count);
        if (pool.Count == 0) return result;

        var addresses = pool.Select(p => p.PublicKey).ToList();
        var lamports = await GetLamportsAsync(addresses, ct);

        var atas = pool.Select(p => DeriveAta(p.PublicKey)).ToList();
        var tokens = await GetTokenAmountsAsync(atas, ct);

        for (var i = 0; i < pool.Count; i++)
            result[pool[i].PublicKey] = new WalletBalances(lamports[i], tokens[i]);

        return result;
    }

    /// <summary>
    /// The fresh single read taken at execution time. The polled figure is up to
    /// refreshIntervalSeconds stale, and "sell 100%" has to mean 100% of what is actually there.
    /// </summary>
    public async Task<ulong> GetTokenBalanceAsync(string owner, CancellationToken ct)
    {
        var amounts = await GetTokenAmountsAsync([DeriveAta(owner)], ct);
        return amounts[0];
    }

    public string DeriveAta(string owner)
    {
        var ata = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(new PublicKey(owner), new PublicKey(mint));

        return ata?.Key ?? throw new NightfallException($"Could not derive the token account for {owner}.");
    }

    private async Task<List<long>> GetLamportsAsync(List<string> addresses, CancellationToken ct)
    {
        var values = new List<long>(addresses.Count);

        foreach (var chunk in Chunk(addresses))
        {
            var result = await rpc.GetMultipleAccountsAsync(chunk, Commitment.Confirmed);

            if (!result.WasSuccessful)
                throw new NightfallException($"Could not read wallet balances from the RPC endpoint. ({result.Reason})");

            // A null entry means the account does not exist on chain yet — zero lamports.
            values.AddRange(result.Result.Value.Select(a => a is null ? 0L : (long)a.Lamports));
        }

        return values;
    }

    private async Task<List<ulong>> GetTokenAmountsAsync(List<string> tokenAccounts, CancellationToken ct)
    {
        var values = new List<ulong>(tokenAccounts.Count);

        foreach (var chunk in Chunk(tokenAccounts))
        {
            var result = await rpc.GetMultipleAccountsAsync(chunk, Commitment.Confirmed);

            if (!result.WasSuccessful)
                throw new NightfallException($"Could not read token balances from the RPC endpoint. ({result.Reason})");

            foreach (var account in result.Result.Value)
                values.Add(account is null ? 0UL : ReadSplAmount(account.Data));
        }

        return values;
    }

    /// <summary>
    /// The SPL token account layout puts the u64 amount at byte 64 (mint 32 || owner 32 || amount 8).
    /// Read straight from base64 account data so one getMultipleAccounts covers the whole pool —
    /// getTokenAccountBalance would be one round trip per wallet.
    /// </summary>
    private static ulong ReadSplAmount(IList<string>? data)
    {
        if (data is null || data.Count == 0) return 0UL;

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(data[0]);
        }
        catch (FormatException)
        {
            return 0UL;
        }

        return bytes.Length < 72 ? 0UL : BitConverter.ToUInt64(bytes, 64);
    }

    // getMultipleAccounts caps at 100 keys per call.
    private static IEnumerable<List<string>> Chunk(List<string> source)
    {
        for (var i = 0; i < source.Count; i += 100)
            yield return source.GetRange(i, Math.Min(100, source.Count - i));
    }
}
