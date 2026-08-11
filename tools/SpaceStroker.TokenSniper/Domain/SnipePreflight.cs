using Solnet.Wallet;
using Solnet.Wallet.Utilities;

namespace SpaceStroker.TokenSniper.Domain;

public sealed record PreflightResult(bool IsEmpty, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool Ok => !IsEmpty && Errors.Count == 0;
}

public static class SnipePreflight
{
    /// <summary>
    /// Every check runs before buy #1; any error aborts the run. Collects ALL problems rather than
    /// throwing on the first, so one dry run is enough to fix the whole file. The curve and supply
    /// arrive as VALUES (not an RPC client) so this stays pure and unit-testable.
    /// </summary>
    public static PreflightResult Check(
        IReadOnlyList<PoolEntry> pool,
        CurveState? curve,
        ulong mintTotalSupply)
    {
        if (pool.Count == 0)
            return new PreflightResult(IsEmpty: true, Array.Empty<string>(), Array.Empty<string>());

        var errors = new List<string>();
        var warnings = new List<string>();

        foreach (var entry in pool)
        {
            var label = string.IsNullOrWhiteSpace(entry.Alias) ? $"<blank alias> ({entry.PublicKey})" : entry.Alias;

            if (string.IsNullOrWhiteSpace(entry.Alias))
                errors.Add($"{label}: alias is required — it is the execution order key, so it is never inferred.");

            if (!IsValidSolanaAddress(entry.PublicKey))
                errors.Add($"{label}: '{entry.PublicKey}' is not a valid Solana address.");

            if (entry.SupplyPercentage <= 0)
                errors.Add($"{label}: SupplyPercentage must be greater than zero (was {SnipePool.FormatPercent(entry.SupplyPercentage)}).");

            if (string.IsNullOrWhiteSpace(entry.PrivateKey))
                errors.Add($"{label}: PrivateKey is required — this tool signs each wallet's own buy.");
            else if (!KeyPairMatches(entry.PrivateKey, entry.PublicKey))
                errors.Add($"{label}: the PrivateKey does not derive the PublicKey on this entry.");
        }

        foreach (var duplicate in pool.Where(e => !string.IsNullOrWhiteSpace(e.Alias))
                     .GroupBy(e => e.Alias, StringComparer.Ordinal).Where(g => g.Count() > 1))
            errors.Add($"Duplicate alias '{duplicate.Key}' appears {duplicate.Count()} times — the ordering key must be unique.");

        foreach (var duplicate in pool.GroupBy(e => e.PublicKey, StringComparer.Ordinal).Where(g => g.Count() > 1))
            errors.Add($"Duplicate address {duplicate.Key} appears {duplicate.Count()} times " +
                       $"({string.Join(", ", duplicate.Select(e => e.Alias))}).");

        if (BuySchedule.MixesPaddedAndUnpaddedAliases(pool.Select(e => e.Alias)))
            warnings.Add("The pool mixes zero-padded and unpadded aliases (e.g. W2 and W10). Ordering is " +
                         "ordinal, so W10 executes before W2. Pad them to match your intent.");

        if (curve is null)
            errors.Add("The bonding-curve account for this mint does not exist. Check snipe.mint, or the token graduated and the curve was closed.");
        else
        {
            if (BondingCurve.IsGraduated(curve))
                errors.Add("This token has already graduated from the bonding curve — buys must go to the AMM, which this tool deliberately does not support.");

            if (!BondingCurve.IsSolQuoted(curve))
                errors.Add($"This curve is quoted in {curve.QuoteMint}, not SOL. This tool prices and pays in SOL only.");

            var totalTargets = pool.Aggregate(0ul, (sum, e) => sum + SnipePlan.TargetFor(e.SupplyPercentage, mintTotalSupply));
            if (curve is not null && totalTargets > curve.RealTokenReserves)
                errors.Add($"The pool targets {totalTargets} base units but the curve can only sell {curve.RealTokenReserves}. " +
                           "Lower the percentages or remove wallets.");
        }

        if (mintTotalSupply == 0)
            errors.Add("The mint reports a total supply of zero — nothing can be targeted against it.");

        return new PreflightResult(IsEmpty: false, errors, warnings);
    }

    /// <summary>
    /// Solnet's `new PublicKey(string)` does NOT validate — monkify-backend only ever calls it on
    /// values already known to be addresses. Validate explicitly and assert the 32-byte decode: a
    /// truncated base58 string is exactly the mistyped-address case, and it is irreversible.
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

    /// <summary>
    /// A Solana secret key is 64 bytes: the 32-byte seed followed by its own public key. Comparing the
    /// tail against the declared PublicKey catches a pool entry whose two halves were pasted from
    /// different wallets — which would otherwise sign a buy that credits somebody else's address.
    /// </summary>
    public static bool KeyPairMatches(string privateKey, string publicKey)
    {
        if (!IsValidSolanaAddress(publicKey)) return false;

        try
        {
            var secret = new Base58Encoder().DecodeData(privateKey);
            if (secret.Length != 64) return false;

            var declared = new PublicKey(publicKey).KeyBytes;
            return secret.AsSpan(32, 32).SequenceEqual(declared);
        }
        catch
        {
            return false;
        }
    }
}
