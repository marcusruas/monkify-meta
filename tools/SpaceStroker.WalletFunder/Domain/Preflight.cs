using Solnet.Wallet;

namespace SpaceStroker.WalletFunder.Domain;

public sealed record PreflightResult(bool IsEmpty, IReadOnlyList<string> Errors)
{
    public bool Ok => !IsEmpty && Errors.Count == 0;
}

public static class Preflight
{
    /// <summary>
    /// Every check runs before send #1; any failure aborts the run. Collects ALL problems rather than
    /// throwing on the first, so one dry run is enough to fix the whole file.
    /// The balance arrives as a VALUE (not a Coinbase client) so this stays pure and unit-testable —
    /// the card rules out a fake sender, and these are the rules that most need covering.
    /// </summary>
    public static PreflightResult Check(
        IReadOnlyList<PoolEntry> pool, decimal availableSol, decimal minAmountSol, decimal feeHeadroomSol)
    {
        if (pool.Count == 0) return new PreflightResult(IsEmpty: true, Array.Empty<string>());

        var errors = new List<string>();

        foreach (var entry in pool)
        {
            if (!IsValidSolanaAddress(entry.PublicKey))
                errors.Add($"{entry.Label}: '{entry.PublicKey}' is not a valid Solana address.");

            if (entry.SolAmount <= 0)
                errors.Add($"{entry.Label}: amount must be greater than zero (was {WalletPool.FormatSol(entry.SolAmount)}).");
            else if (entry.SolAmount < minAmountSol)
                errors.Add($"{entry.Label}: amount {WalletPool.FormatSol(entry.SolAmount)} SOL is below the " +
                           $"minimum {WalletPool.FormatSol(minAmountSol)} SOL.");
        }

        foreach (var duplicate in pool.GroupBy(e => e.PublicKey, StringComparer.Ordinal).Where(g => g.Count() > 1))
            errors.Add($"Duplicate address {duplicate.Key} appears {duplicate.Count()} times " +
                       $"({string.Join(", ", duplicate.Select(e => e.Label))}).");

        var total = pool.Sum(e => e.SolAmount);
        var required = total + feeHeadroomSol;
        if (availableSol < required)
            errors.Add($"Coinbase SOL balance {WalletPool.FormatSol(availableSol)} does not cover the batch total " +
                       $"{WalletPool.FormatSol(total)} plus the {WalletPool.FormatSol(feeHeadroomSol)} network-fee " +
                       $"headroom (short by {WalletPool.FormatSol(required - availableSol)} SOL).");

        return new PreflightResult(IsEmpty: false, errors);
    }

    /// <summary>
    /// Solnet's `new PublicKey(string)` does NOT validate — monkify-backend only ever calls it on values
    /// already known to be addresses. Validate explicitly, and assert the 32-byte decode: a truncated
    /// base58 string is precisely the mistyped-address case, and it is irreversible if it gets through.
    /// </summary>
    public static bool IsValidSolanaAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        if (!PublicKey.IsValid(address)) return false;

        try
        {
            return new PublicKey(address).KeyBytes.Length == 32;
        }
        catch
        {
            return false;
        }
    }
}
