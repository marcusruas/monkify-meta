using Shouldly;
using SpaceStroker.TokenSniper.Domain;
using Xunit;

namespace SpaceStroker.TokenSniper.Tests.Domain;

public class SnipePlanTests
{
    private const ulong OneBillionTokens = 1_000_000_000_000_000;   // 1B with 6 decimals
    private static readonly CurveFees OnePercent = new(95, 5);

    [Fact]
    public void TargetFor_TreatsSupplyPercentageAsAPercentNumberNotAFraction()
    {
        // The card's example: 0.4504 means 0.4504% of total supply — NOT 45.04%.
        SnipePlan.TargetFor(0.4504m, OneBillionTokens).ShouldBe(4_504_000_000_000ul);
        SnipePlan.TargetFor(0.4504m, OneBillionTokens).ShouldNotBe(450_400_000_000_000ul);
    }

    [Fact]
    public void TargetFor_TruncatesSoAWalletIsNeverPlannedAboveItsShare()
    {
        SnipePlan.TargetFor(0.0000001m, 1_000_000ul).ShouldBe(0ul);
        SnipePlan.TargetFor(33.333333m, 1_000_000ul).ShouldBe(333_333ul);
    }

    [Fact]
    public void TargetFor_IsZeroForANonPositivePercentage()
    {
        SnipePlan.TargetFor(0m, OneBillionTokens).ShouldBe(0ul);
        SnipePlan.TargetFor(-1m, OneBillionTokens).ShouldBe(0ul);
    }

    [Fact]
    public void Build_SkipsAWalletThatAlreadyHoldsItsTarget()
    {
        var target = new WalletTarget("W01", "addr", 1m, TargetBaseUnits: 100, HeldBaseUnits: 100);

        var plan = SnipePlan.Build([target], Funding("W01", 5_000_000_000), Curve(), OnePercent, 0.25m, 15_000_000);

        plan[0].IsSkip.ShouldBeTrue();
        plan[0].SkipReason.ShouldBe("already at target");
        plan[0].QuotedCostLamports.ShouldBe(0ul);
    }

    [Fact]
    public void Build_BuysOnlyTheShortfallOnAResumedRun()
    {
        var target = new WalletTarget("W01", "addr", 1m, TargetBaseUnits: 1_000, HeldBaseUnits: 600);

        var plan = SnipePlan.Build([target], Funding("W01", 5_000_000_000), Curve(), OnePercent, 0.25m, 15_000_000);

        plan[0].TokensToBuy.ShouldBe(400ul);
    }

    [Fact]
    public void Build_CapsAWalletThatCannotAffordItsTargetAndFlagsIt()
    {
        // 200M tokens (20% of supply) costs ~28 SOL against this curve; the wallet holds 15,000,000
        // lamports of reserve plus 1 SOL of spendable, so the target is far out of reach.
        var target = new WalletTarget("W01", "addr", 1m, TargetBaseUnits: 200_000_000_000_000, HeldBaseUnits: 0);

        var plan = SnipePlan.Build([target], Funding("W01", 1_015_000_000), Curve(), OnePercent, 0.25m, 15_000_000);

        plan[0].CappedByBalance.ShouldBeTrue();
        plan[0].TokensToBuy.ShouldBeLessThan(200_000_000_000_000ul);
        plan[0].TokensToBuy.ShouldBeGreaterThan(0ul);
        plan[0].QuotedCostLamports.ShouldBeLessThanOrEqualTo(1_000_000_000ul);
    }

    [Fact]
    public void Build_SkipsAWalletWithNoSpendableSolAfterTheReserve()
    {
        var target = new WalletTarget("W01", "addr", 1m, TargetBaseUnits: 500, HeldBaseUnits: 0);

        var plan = SnipePlan.Build([target], Funding("W01", 1_000), Curve(), OnePercent, 0.25m, 15_000_000);

        plan[0].IsSkip.ShouldBeTrue();
        plan[0].CappedByBalance.ShouldBeTrue();
        plan[0].SkipReason.ShouldContain("spendable");
    }

    [Fact]
    public void Build_WithholdsAssociatedTokenAccountRentWhenTheAccountIsMissing()
    {
        // Target far beyond the balance, so the SPENDABLE lamports are what decide the size — which is
        // exactly where the missing account's rent has to be withheld or the last wallet cannot pay.
        var target = new WalletTarget("W01", "addr", 1m, TargetBaseUnits: 200_000_000_000_000, HeldBaseUnits: 0);

        var withAta = SnipePlan.Build([target],
            new Dictionary<string, WalletFunding> { ["W01"] = new(18_000_000, false) },
            Curve(), OnePercent, 0.25m, 15_000_000)[0];

        var withoutAta = SnipePlan.Build([target],
            new Dictionary<string, WalletFunding> { ["W01"] = new(18_000_000, true) },
            Curve(), OnePercent, 0.25m, 15_000_000)[0];

        withAta.TokensToBuy.ShouldBeGreaterThan(0ul);
        withoutAta.TokensToBuy.ShouldBeLessThan(withAta.TokensToBuy);
    }

    [Fact]
    public void Build_QuotesTheLadderSequentiallySoLaterWalletsPayMore()
    {
        var targets = new[]
        {
            new WalletTarget("W01", "a", 1m, 10_000_000_000, 0),
            new WalletTarget("W02", "b", 1m, 10_000_000_000, 0),
            new WalletTarget("W03", "c", 1m, 10_000_000_000, 0),
        };

        var funding = targets.ToDictionary(t => t.Alias, _ => new WalletFunding(500_000_000_000, false));

        var plan = SnipePlan.Build(targets, funding, Curve(), OnePercent, 0.25m, 15_000_000);

        plan[1].QuotedCostLamports.ShouldBeGreaterThan(plan[0].QuotedCostLamports);
        plan[2].QuotedCostLamports.ShouldBeGreaterThan(plan[1].QuotedCostLamports);
    }

    [Fact]
    public void Build_SetsMaxSolCostToTheQuotePlusToleranceButNeverAboveWhatTheWalletHolds()
    {
        var target = new WalletTarget("W01", "addr", 1m, TargetBaseUnits: 1_000, HeldBaseUnits: 0);

        var plan = SnipePlan.Build([target], Funding("W01", 500_000_000_000), Curve(), OnePercent, 0.25m, 15_000_000);

        plan[0].MaxSolCostLamports.ShouldBe(SnipePlan.WithSlippage(plan[0].QuotedCostLamports, 0.25m));
        plan[0].MaxSolCostLamports.ShouldBeGreaterThanOrEqualTo(plan[0].QuotedCostLamports);
    }

    [Fact]
    public void Build_ThrowsWhenAWalletHasNoFundingSnapshot()
    {
        var target = new WalletTarget("W01", "addr", 1m, 1_000, 0);

        Should.Throw<SniperException>(() =>
            SnipePlan.Build([target], new Dictionary<string, WalletFunding>(), Curve(), OnePercent, 0.25m, 0));
    }

    [Fact]
    public void WithSlippage_RoundsUpAndIsANoOpAtZeroTolerance()
    {
        SnipePlan.WithSlippage(1_000, 0.25m).ShouldBe(1_250ul);
        SnipePlan.WithSlippage(3, 0.1m).ShouldBe(4ul);          // 3.3 ⇒ 4
        SnipePlan.WithSlippage(1_000, 0m).ShouldBe(1_000ul);
    }

    private static Dictionary<string, WalletFunding> Funding(string alias, ulong lamports) =>
        new() { [alias] = new WalletFunding(lamports, false) };

    private static CurveState Curve() =>
        new(587_705_864_083_302, 54_772_298_209, 307_805_864_083_302, 24_772_298_209,
            OneBillionTokens, false, "69uK17vg5VqGQ7xVRRRhGgGeddhvXiTKREmbnXRDaZLV",
            false, false, BondingCurve.SolQuoteMint);
}
