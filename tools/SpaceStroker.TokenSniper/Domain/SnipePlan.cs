namespace SpaceStroker.TokenSniper.Domain;

/// <summary>What one wallet should hold, and what it holds already.</summary>
public sealed record WalletTarget(
    string Alias,
    string PublicKey,
    decimal SupplyPercentage,
    ulong TargetBaseUnits,
    ulong HeldBaseUnits)
{
    public ulong ShortfallBaseUnits => HeldBaseUnits >= TargetBaseUnits ? 0 : TargetBaseUnits - HeldBaseUnits;
}

/// <summary>On-chain funding facts for a wallet, passed into the plan as values.</summary>
public sealed record WalletFunding(ulong LamportBalance, bool NeedsAssociatedTokenAccount);

public sealed record PlannedBuy(
    WalletTarget Target,
    ulong TokensToBuy,
    ulong QuotedCostLamports,
    ulong MaxSolCostLamports,
    bool CappedByBalance,
    string? SkipReason = null)
{
    public bool IsSkip => TokensToBuy == 0;
}

public static class SnipePlan
{
    /// <summary>Rent-exempt minimum for an SPL token account (165 bytes). Withheld from the buy budget.</summary>
    public const ulong AssociatedTokenAccountRentLamports = 2_039_280;

    /// <summary>
    /// SupplyPercentage is a PERCENT NUMBER of total supply: 0.4504 ⇒ 0.4504%, never 45.04%.
    /// Truncated, not rounded — a wallet is never planned above its share.
    /// </summary>
    public static ulong TargetFor(decimal supplyPercentage, ulong totalSupplyBaseUnits)
    {
        if (supplyPercentage <= 0) return 0;

        var target = decimal.Floor((decimal)totalSupplyBaseUnits * supplyPercentage / 100m);
        return target <= 0 ? 0 : (ulong)target;
    }

    /// <summary>
    /// Quote the whole ladder in execution order, advancing the curve after each wallet.
    ///
    /// Decision 7 on the card: the supply target wins over budget — there is no global SOL cap, and a
    /// wallet is bounded only by what it holds. A wallet that cannot afford its shortfall buys the
    /// inverse-priced maximum it CAN afford and is flagged <see cref="PlannedBuy.CappedByBalance"/>.
    /// </summary>
    public static IReadOnlyList<PlannedBuy> Build(
        IReadOnlyList<WalletTarget> targets,
        IReadOnlyDictionary<string, WalletFunding> funding,
        CurveState curve,
        CurveFees fees,
        decimal slippageTolerance,
        ulong reserveLamports)
    {
        var plan = new List<PlannedBuy>(targets.Count);
        var state = curve;

        foreach (var target in targets)
        {
            if (target.ShortfallBaseUnits == 0)
            {
                plan.Add(new PlannedBuy(target, 0, 0, 0, false, "already at target"));
                continue;
            }

            if (!funding.TryGetValue(target.Alias, out var wallet))
                throw new SniperException($"{target.Alias}: no funding snapshot was taken for this wallet.");

            var overhead = reserveLamports +
                           (wallet.NeedsAssociatedTokenAccount ? AssociatedTokenAccountRentLamports : 0);
            var spendable = wallet.LamportBalance > overhead ? wallet.LamportBalance - overhead : 0;

            if (spendable == 0)
            {
                plan.Add(new PlannedBuy(target, 0, 0, 0, true, "no spendable SOL after fee and rent headroom"));
                continue;
            }

            var tokens = Math.Min(target.ShortfallBaseUnits, state.RealTokenReserves);
            var capped = tokens < target.ShortfallBaseUnits;

            var cost = BondingCurve.CostForTokens(state, fees, tokens);
            if (cost > spendable)
            {
                tokens = BondingCurve.TokensForBudget(state, fees, spendable);
                capped = true;

                if (tokens == 0)
                {
                    plan.Add(new PlannedBuy(target, 0, 0, 0, true, "balance too small to buy a single base unit"));
                    continue;
                }

                cost = BondingCurve.CostForTokens(state, fees, tokens);
            }

            var maxSolCost = WithSlippage(cost, slippageTolerance);
            if (maxSolCost > spendable) maxSolCost = spendable;   // never authorise more than the wallet holds
            if (maxSolCost < cost) maxSolCost = cost;

            plan.Add(new PlannedBuy(target, tokens, cost, maxSolCost, capped));

            state = BondingCurve.AfterBuy(state, tokens, BondingCurve.SolInForTokens(state, tokens));
        }

        return plan;
    }

    /// <summary>
    /// The on-chain ceiling for a single transaction. NOT a budget cap — it is what stops one buy
    /// being sandwiched into paying many times its quote, and the instruction cannot be built without it.
    /// </summary>
    public static ulong WithSlippage(ulong lamports, decimal tolerance)
    {
        if (tolerance <= 0) return lamports;

        var withSlippage = decimal.Ceiling(lamports * (1m + tolerance));
        return withSlippage >= ulong.MaxValue ? ulong.MaxValue : (ulong)withSlippage;
    }
}
