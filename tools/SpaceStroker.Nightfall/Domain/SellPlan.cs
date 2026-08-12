namespace SpaceStroker.Nightfall.Domain;

/// <summary>
/// Turns the dashboard's single SELL % field into a raw token amount, per wallet.
///
/// The percentage is of the WALLET'S OWN balance, never of mint supply: a wallet holding 2% of
/// supply at SELL % = 40 sells 0.8% of supply. Blank means 100%.
/// </summary>
public static class SellPlan
{
    public static ulong AmountFor(ulong rawBalance, decimal? percent)
    {
        if (rawBalance == 0UL)
            throw new NightfallException(SellPreflight.Describe(SellExclusion.ZeroBalance));

        if (percent is null)
            return rawBalance;

        if (percent is <= 0m or > 100m)
            throw new NightfallException($"SELL % must be greater than 0 and at most 100 (was {TokenAmount.Format(percent.Value)}).");

        if (percent == 100m)
            return rawBalance;

        // Decimal, then floor. Rounding UP would ask the chain to move more than the wallet holds,
        // which fails the whole transaction after we have already paid for a quote.
        var scaled = decimal.Floor(rawBalance * percent.Value / 100m);
        var amount = (ulong)scaled;

        if (amount == 0UL)
            throw new NightfallException(
                $"{TokenAmount.Format(percent.Value)}% of this wallet's balance rounds to zero tokens — nothing to sell.");

        return amount;
    }
}
