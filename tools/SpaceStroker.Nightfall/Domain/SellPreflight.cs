namespace SpaceStroker.Nightfall.Domain;

public enum SellExclusion
{
    None,
    ZeroBalance,
    NotEnoughSolForFee,
    PriceImpactTooHigh,
}

/// <summary>
/// The gates a wallet must clear before anything is signed. Pure by design — every rule here takes
/// values, never an RPC client or an HTTP client, which is what makes the money-safety behaviour
/// testable without a fake sender (the same reason Preflight.Check in the funder takes a decimal).
/// </summary>
public static class SellPreflight
{
    public static SellExclusion Check(ulong tokenRaw, long solLamports, decimal minSolForFee)
    {
        if (tokenRaw == 0UL)
            return SellExclusion.ZeroBalance;

        if (solLamports < TokenAmount.SolToLamports(minSolForFee))
            return SellExclusion.NotEnoughSolForFee;

        return SellExclusion.None;
    }

    /// <summary>
    /// Runs on the quote, BEFORE the swap is built or broadcast. Slippage alone does not cover this:
    /// a wallet can dump into near-zero liquidity at a terrible price that is still inside tolerance.
    /// </summary>
    public static SellExclusion CheckPriceImpact(decimal priceImpactPercent, decimal maxPercent)
        => priceImpactPercent > maxPercent ? SellExclusion.PriceImpactTooHigh : SellExclusion.None;

    public static string Describe(SellExclusion exclusion) => exclusion switch
    {
        SellExclusion.None => "Ready to sell.",
        SellExclusion.ZeroBalance => "This wallet holds none of the token.",
        SellExclusion.NotEnoughSolForFee =>
            "Not enough SOL to pay the transaction fee — top this wallet up with spacestroker-fund-wallets.",
        SellExclusion.PriceImpactTooHigh =>
            "The quote's price impact is above the configured maximum, so nothing was broadcast.",
        _ => "Excluded.",
    };
}
