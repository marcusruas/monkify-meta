namespace SpaceStroker.TokenSniper.Domain;

/// <summary>
/// The pump.fun bonding-curve account, decoded.
///
/// ⚠️ Field names come from the program's OWN on-chain Anchor IDL (read 2026-08-11), not from
/// ecosystem documentation: the quote side is <c>virtual_quote_reserves</c>/<c>real_quote_reserves</c>,
/// NOT "sol_reserves", and the account now also carries <c>creator</c>, <c>is_mayhem_mode</c>,
/// <c>is_cashback_coin</c> and <c>quote_mint</c>. A curve whose <c>quote_mint</c> is not the default
/// pubkey is priced in some OTHER token — this tool refuses those rather than quoting them as SOL.
/// </summary>
public sealed record CurveState(
    ulong VirtualTokenReserves,
    ulong VirtualQuoteReserves,
    ulong RealTokenReserves,
    ulong RealQuoteReserves,
    ulong TokenTotalSupply,
    bool Complete,
    string Creator,
    bool IsMayhemMode,
    bool IsCashbackCoin,
    string QuoteMint);

/// <summary>
/// Trade fees, read from the program's <c>global</c> account at runtime — never hardcoded.
/// On 2026-08-11 mainnet: 95 bps platform + 5 bps creator = 1%.
/// </summary>
public sealed record CurveFees(ulong FeeBasisPoints, ulong CreatorFeeBasisPoints)
{
    public ulong TotalBasisPoints => FeeBasisPoints + CreatorFeeBasisPoints;
}

public static class BondingCurve
{
    /// <summary>The default pubkey (all zero bytes). A curve quoted in SOL carries this as its quote mint.</summary>
    public const string SolQuoteMint = "11111111111111111111111111111111";

    private const ulong BasisPointScale = 10_000;

    /// <summary>
    /// Graduated ⇒ the curve is closed and liquidity has migrated; buys must stop. Both forms are
    /// checked because the flag and the reserves are set by different code paths on the program side.
    /// </summary>
    public static bool IsGraduated(CurveState curve) => curve.Complete || curve.RealTokenReserves == 0;

    public static bool IsSolQuoted(CurveState curve) =>
        string.Equals(curve.QuoteMint, SolQuoteMint, StringComparison.Ordinal);

    /// <summary>
    /// Lamports required to receive <paramref name="tokens"/> base units, fees included.
    ///
    /// Constant product over the VIRTUAL reserves: k = vTokens · vQuote, and buying t tokens moves the
    /// quote reserve to k / (vTokens − t). Fees ride ON TOP of that (the pool receives the pre-fee
    /// amount). Integer math throughout — never double — and the cost rounds UP so the operator is
    /// never quoted less than they pay.
    /// </summary>
    public static ulong CostForTokens(CurveState curve, CurveFees fees, ulong tokens)
    {
        if (tokens == 0) return 0;

        if (tokens > curve.RealTokenReserves)
            throw new SniperException(
                $"The curve holds {curve.RealTokenReserves} base units and cannot sell {tokens}.");

        if (tokens >= curve.VirtualTokenReserves)
            throw new SniperException("The requested amount exceeds the curve's virtual token reserves.");

        var solIn = SolInForTokens(curve, tokens);
        return solIn + FeeOn(solIn, fees);
    }

    /// <summary>The pre-fee lamports that actually enter the pool.</summary>
    public static ulong SolInForTokens(CurveState curve, ulong tokens)
    {
        UInt128 k = (UInt128)curve.VirtualTokenReserves * curve.VirtualQuoteReserves;
        var newQuote = CeilDiv(k, (UInt128)curve.VirtualTokenReserves - tokens);
        return (ulong)(newQuote - curve.VirtualQuoteReserves);
    }

    /// <summary>
    /// The inverse: how many base units <paramref name="lamports"/> buys, fees included. Rounds DOWN
    /// and then verifies against <see cref="CostForTokens"/>, stepping back if integer rounding
    /// overshot the budget — the affordability path must never authorise more than the wallet holds.
    /// </summary>
    public static ulong TokensForBudget(CurveState curve, CurveFees fees, ulong lamports)
    {
        if (lamports == 0) return 0;

        // Strip the fee that will ride on top: solNet · (scale + bps) / scale ≤ lamports.
        var solNet = (ulong)((UInt128)lamports * BasisPointScale / (BasisPointScale + fees.TotalBasisPoints));
        if (solNet == 0) return 0;

        UInt128 k = (UInt128)curve.VirtualTokenReserves * curve.VirtualQuoteReserves;
        var newTokenReserves = CeilDiv(k, (UInt128)curve.VirtualQuoteReserves + solNet);
        if (newTokenReserves >= curve.VirtualTokenReserves) return 0;

        var tokens = (ulong)((UInt128)curve.VirtualTokenReserves - newTokenReserves);
        if (tokens > curve.RealTokenReserves) tokens = curve.RealTokenReserves;

        // Integer rounding can leave the quote a lamport or two above the budget. Step down until it fits.
        while (tokens > 0 && CostForTokens(curve, fees, tokens) > lamports) tokens--;

        return tokens;
    }

    /// <summary>
    /// The curve after a buy lands. Used to quote the ladder SEQUENTIALLY: every wallet after the
    /// first must be priced against the reserves its predecessors left behind, or the plan understates
    /// the total by a wide margin on a large pool.
    /// </summary>
    public static CurveState AfterBuy(CurveState curve, ulong tokens, ulong solIn) =>
        curve with
        {
            VirtualTokenReserves = curve.VirtualTokenReserves - tokens,
            VirtualQuoteReserves = curve.VirtualQuoteReserves + solIn,
            RealTokenReserves = curve.RealTokenReserves - tokens,
            RealQuoteReserves = curve.RealQuoteReserves + solIn,
        };

    public static ulong FeeOn(ulong solIn, CurveFees fees) =>
        (ulong)CeilDiv((UInt128)solIn * fees.TotalBasisPoints, BasisPointScale);

    private static UInt128 CeilDiv(UInt128 numerator, UInt128 denominator) =>
        (numerator + denominator - 1) / denominator;
}
