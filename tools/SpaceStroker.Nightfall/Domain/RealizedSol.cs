namespace SpaceStroker.Nightfall.Domain;

/// <summary>
/// What the sale actually paid, read off the confirmed transaction rather than taken from the quote.
/// The quote is what we were promised; this is what we got, and after a concurrent batch the gap
/// between the two is the number an operator actually wants.
/// </summary>
public static class RealizedSol
{
    /// <summary>
    /// (post - pre) + fee, in lamports, for the wallet's account.
    ///
    /// The fee is added back because the wallet is the fee payer: without it the figure understates
    /// the sale by the transaction cost and two sells of the same size appear to pay differently.
    /// </summary>
    public static long From(long[] preBalances, long[] postBalances, long fee, int accountIndex)
    {
        if (preBalances.Length != postBalances.Length)
            throw new NightfallException(
                $"The confirmed transaction reported {preBalances.Length} pre-balances and {postBalances.Length} post-balances.");

        if (accountIndex < 0 || accountIndex >= preBalances.Length)
            throw new NightfallException(
                $"The confirmed transaction has no account at index {accountIndex}; the realized amount cannot be read.");

        return postBalances[accountIndex] - preBalances[accountIndex] + fee;
    }
}
