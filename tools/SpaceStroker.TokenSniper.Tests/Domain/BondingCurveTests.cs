using Shouldly;
using SpaceStroker.TokenSniper.Domain;
using Xunit;

namespace SpaceStroker.TokenSniper.Tests.Domain;

public class BondingCurveTests
{
    private static readonly CurveFees OnePercent = new(95, 5);

    /// <summary>
    /// Small, hand-checkable reserves: k = 1,000,000. Buying 100 tokens moves the quote reserve to
    /// ceil(1,000,000 / 900) = 1,112, so 112 lamports enter the pool and the 1% fee adds 2 (rounded up).
    /// </summary>
    [Fact]
    public void CostForTokens_IsConstantProductOverVirtualReservesPlusFees()
    {
        var curve = Curve(virtualTokens: 1_000, virtualQuote: 1_000, realTokens: 1_000);

        BondingCurve.SolInForTokens(curve, 100).ShouldBe(112ul);
        BondingCurve.CostForTokens(curve, OnePercent, 100).ShouldBe(114ul);
    }

    [Fact]
    public void CostForTokens_RoundsUpSoTheQuoteIsNeverBelowWhatIsPaid()
    {
        var curve = Curve(virtualTokens: 1_000, virtualQuote: 1_000, realTokens: 1_000);

        // 1,000,000 / 999 = 1001.001… ⇒ 1002 - 1000 = 2 lamports in, not 1.
        BondingCurve.SolInForTokens(curve, 1).ShouldBe(2ul);
    }

    [Fact]
    public void CostForTokens_IsZeroForZeroTokens()
    {
        BondingCurve.CostForTokens(Curve(1_000, 1_000, 1_000), OnePercent, 0).ShouldBe(0ul);
    }

    [Fact]
    public void CostForTokens_RefusesMoreThanTheCurveHolds()
    {
        var curve = Curve(virtualTokens: 1_000, virtualQuote: 1_000, realTokens: 500);

        Should.Throw<SniperException>(() => BondingCurve.CostForTokens(curve, OnePercent, 501));
    }

    [Fact]
    public void TokensForBudget_InvertsCostForTokens()
    {
        var curve = Curve(virtualTokens: 1_000, virtualQuote: 1_000, realTokens: 1_000);

        BondingCurve.TokensForBudget(curve, OnePercent, 114).ShouldBe(100ul);
    }

    [Fact]
    public void TokensForBudget_NeverCostsMoreThanTheBudget()
    {
        var curve = Curve(virtualTokens: 587_705_864_083_302, virtualQuote: 54_772_298_209, realTokens: 307_805_864_083_302);

        foreach (var budget in new ulong[] { 1_000, 1_000_000, 250_000_000, 3_000_000_000 })
        {
            var tokens = BondingCurve.TokensForBudget(curve, OnePercent, budget);
            BondingCurve.CostForTokens(curve, OnePercent, tokens).ShouldBeLessThanOrEqualTo(budget);
        }
    }

    [Fact]
    public void TokensForBudget_IsCappedByTheCurvesRealTokenReserves()
    {
        var curve = Curve(virtualTokens: 1_000, virtualQuote: 1_000, realTokens: 50);

        BondingCurve.TokensForBudget(curve, OnePercent, 100_000).ShouldBe(50ul);
    }

    [Fact]
    public void TokensForBudget_IsZeroWhenTheBudgetCannotCoverASingleBaseUnit()
    {
        BondingCurve.TokensForBudget(Curve(1_000, 1_000, 1_000), OnePercent, 1).ShouldBe(0ul);
    }

    [Fact]
    public void AfterBuy_AdvancesBothSidesSoTheLadderRepricesSequentially()
    {
        var curve = Curve(virtualTokens: 1_000, virtualQuote: 1_000, realTokens: 1_000);
        var first = BondingCurve.CostForTokens(curve, OnePercent, 100);

        var moved = BondingCurve.AfterBuy(curve, 100, BondingCurve.SolInForTokens(curve, 100));
        var second = BondingCurve.CostForTokens(moved, OnePercent, 100);

        moved.VirtualTokenReserves.ShouldBe(900ul);
        moved.VirtualQuoteReserves.ShouldBe(1_112ul);
        moved.RealTokenReserves.ShouldBe(900ul);
        second.ShouldBeGreaterThan(first);
    }

    [Fact]
    public void IsGraduated_WhenCompleteOrOutOfRealTokens()
    {
        BondingCurve.IsGraduated(Curve(1_000, 1_000, 1_000)).ShouldBeFalse();
        BondingCurve.IsGraduated(Curve(1_000, 1_000, 1_000) with { Complete = true }).ShouldBeTrue();
        BondingCurve.IsGraduated(Curve(1_000, 1_000, realTokens: 0)).ShouldBeTrue();
    }

    [Fact]
    public void IsSolQuoted_OnlyForTheDefaultQuoteMint()
    {
        BondingCurve.IsSolQuoted(Curve(1, 1, 1)).ShouldBeTrue();
        BondingCurve.IsSolQuoted(Curve(1, 1, 1) with { QuoteMint = "So11111111111111111111111111111111111111112" })
            .ShouldBeFalse();
    }

    [Fact]
    public void FeeOn_RoundsUpAndCoversBothRates()
    {
        BondingCurve.FeeOn(10_000, new CurveFees(95, 5)).ShouldBe(100ul);
        BondingCurve.FeeOn(1, new CurveFees(95, 5)).ShouldBe(1ul);
        BondingCurve.FeeOn(1_000, new CurveFees(0, 0)).ShouldBe(0ul);
    }

    private static CurveState Curve(ulong virtualTokens, ulong virtualQuote, ulong realTokens) =>
        new(virtualTokens, virtualQuote, realTokens, 0, 1_000_000_000_000_000, false,
            "69uK17vg5VqGQ7xVRRRhGgGeddhvXiTKREmbnXRDaZLV", false, false, BondingCurve.SolQuoteMint);
}
