using System.Globalization;
using Solnet.Rpc;
using Solnet.Rpc.Types;
using SpaceStroker.Nightfall.Domain;

namespace SpaceStroker.Nightfall.Infrastructure.Solana;

public sealed record WalletBalances(long SolLamports, ulong TokenRaw);

/// <summary>
/// <paramref name="TokenProgram"/> is read from the mint account's OWNER, never assumed: it is a seed
/// of every associated token account address, so assuming SPL Token makes every Token-2022 balance
/// read as zero. See AssociatedToken.
/// </summary>
public sealed record MintInfo(ulong Supply, int Decimals, string TokenProgram);

/// <summary>
/// Every read the dashboard makes, batched so a 20-wallet pool costs two RPC calls per refresh
/// rather than forty.
/// </summary>
/// <remarks>
/// The token program is a CONSTRUCTOR argument on purpose. It has to be read from the chain before
/// any address can be derived, and taking it here means DeriveAta cannot be called before it is
/// known — read the mint with <see cref="ReadMintAsync"/> first, then build the reader.
/// </remarks>
public sealed class SolanaReader(IRpcClient rpc, string mint, string tokenProgram)
{
    public string TokenProgram { get; } = tokenProgram;

    /// <summary>
    /// Reads supply, decimals and the mint's owning token program. Static because its result is what
    /// a reader needs to exist — see the remarks above.
    /// </summary>
    public static async Task<MintInfo> ReadMintAsync(IRpcClient rpc, string mint, CancellationToken ct)
    {
        // The account read comes first: without it we cannot know which token program owns the mint,
        // and a supply that parses tells us nothing about that.
        var account = await rpc.GetAccountInfoAsync(mint, Commitment.Confirmed);

        if (!account.WasSuccessful || account.Result?.Value is null)
            throw new NightfallException(
                $"Could not read the mint account {mint}. The token mint or the RPC endpoint may be wrong. ({account.Reason})");

        var tokenProgram = AssociatedToken.EnsureTokenProgram(account.Result.Value.Owner, mint);

        var result = await rpc.GetTokenSupplyAsync(mint, Commitment.Confirmed);

        if (!result.WasSuccessful || result.Result?.Value is null)
            throw new NightfallException(
                $"Could not read the supply of {mint}. The token mint or the RPC endpoint may be wrong. ({result.Reason})");

        var supply = ulong.TryParse(result.Result.Value.Amount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new NightfallException($"The RPC returned an unreadable supply for {mint}.");

        return new MintInfo(supply, result.Result.Value.Decimals, tokenProgram);
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

    /// <summary>
    /// ⚠️ Derived with THIS MINT'S token program, never with Solnet's hardcoded SPL Token — see
    /// AssociatedToken for what that costs on a pump.fun Token-2022 mint.
    /// </summary>
    public string DeriveAta(string owner) => AssociatedToken.Derive(owner, mint, TokenProgram);

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
    ///
    /// This is program-agnostic: a Token-2022 account keeps that same 165-byte base layout and appends
    /// its extensions AFTER it (the live pump.fun accounts are 170 bytes), so the amount stays at 64.
    /// Only the ADDRESS differs between the two programs — see AssociatedToken.
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
