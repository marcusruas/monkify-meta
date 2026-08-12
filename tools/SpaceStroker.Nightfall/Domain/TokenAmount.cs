using System.Globalization;

namespace SpaceStroker.Nightfall.Domain;

/// <summary>
/// Conversions between raw base units and display amounts.
///
/// ⚠️ Every method here is deliberately decimal-only. A token position is money, and
/// `raw / Math.Pow(10, decimals)` routes it through a double on the way to the operator's screen —
/// the same reason SolanaService.ToUiAmount builds its divisor with a loop in monkify-backend.
/// </summary>
public static class TokenAmount
{
    public const long LamportsPerSol = 1_000_000_000L;

    /// <summary>Wrapped SOL — the mint every sell routes out to.</summary>
    public const string WrappedSolMint = "So11111111111111111111111111111111111111112";

    public static decimal ToUi(ulong raw, int decimals)
        => raw / PowerOfTen(decimals);

    public static ulong FromUi(decimal ui, int decimals)
        => (ulong)decimal.Truncate(ui * PowerOfTen(decimals));

    public static decimal LamportsToSol(long lamports)
        => lamports / (decimal)LamportsPerSol;

    public static long SolToLamports(decimal sol)
        => (long)decimal.Truncate(sol * LamportsPerSol);

    /// <summary>
    /// Invariant rendering for everything the operator reads. Explicit even though the project sets
    /// InvariantGlobalization — being explicit is what stopped a pt-BR machine emitting "2,54" in #165.
    /// </summary>
    public static string Format(decimal value)
        => value.ToString("0.#########", CultureInfo.InvariantCulture);

    private static decimal PowerOfTen(int exponent)
    {
        if (exponent is < 0 or > 28)
            throw new NightfallException($"A token cannot have {exponent} decimals.");

        var result = 1m;
        for (var i = 0; i < exponent; i++)
            result *= 10m;

        return result;
    }
}
