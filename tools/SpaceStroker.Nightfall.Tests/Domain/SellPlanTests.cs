using Shouldly;
using SpaceStroker.Nightfall.Domain;
using Xunit;

namespace SpaceStroker.Nightfall.Tests.Domain;

public class SellPlanTests
{
    [Fact]
    public void A_null_percent_sells_the_whole_balance()
    {
        // The SELL % field left blank is the card's default and must mean 100%.
        SellPlan.AmountFor(1_000UL, null).ShouldBe(1_000UL);
    }

    [Fact]
    public void One_hundred_percent_sells_the_whole_balance()
    {
        SellPlan.AmountFor(1_000UL, 100m).ShouldBe(1_000UL);
    }

    [Fact]
    public void A_percentage_applies_to_the_wallets_own_balance_not_to_mint_supply()
    {
        // AC12: a wallet holding 2% of supply at SELL % = 40 sells 40% of ITS OWN balance
        // (0.8% of supply), never 40% of supply.
        SellPlan.AmountFor(1_000UL, 40m).ShouldBe(400UL);
    }

    [Fact]
    public void Rounds_down_so_a_sell_never_exceeds_the_balance()
    {
        // 999 * 40% = 399.6. Rounding up would try to sell more than the wallet holds.
        SellPlan.AmountFor(999UL, 40m).ShouldBe(399UL);
    }

    [Fact]
    public void Keeps_full_precision_on_a_large_raw_balance()
    {
        // 123456789012345 * 33.5 / 100 = 41358024319135.575 exactly in decimal, so the floor is
        // ...9135. Through a double the product loses the low digits and the answer drifts.
        SellPlan.AmountFor(123_456_789_012_345UL, 33.5m).ShouldBe(41_358_024_319_135UL);
    }

    [Fact]
    public void Refuses_a_percentage_that_rounds_to_zero_tokens()
    {
        // Silently selling nothing would report a "successful" exit that moved no money.
        var ex = Should.Throw<NightfallException>(() => SellPlan.AmountFor(1UL, 40m));
        ex.Message.ShouldContain("rounds to zero");
    }

    [Fact]
    public void Refuses_a_zero_balance()
    {
        Should.Throw<NightfallException>(() => SellPlan.AmountFor(0UL, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100.01)]
    [InlineData(101)]
    public void Refuses_a_percentage_outside_the_allowed_range(decimal percent)
    {
        var ex = Should.Throw<NightfallException>(() => SellPlan.AmountFor(1_000UL, percent));
        ex.Message.ShouldContain("SELL %");
    }
}

public class SellPreflightTests
{
    private const decimal MinSolForFee = 0.003m;

    [Fact]
    public void A_wallet_holding_none_of_the_token_is_excluded_and_never_quoted()
    {
        // AC17. Jupiter has no route for a zero amount, so quoting it only burns rate limit.
        SellPreflight.Check(tokenRaw: 0UL, solLamports: 1_000_000_000L, MinSolForFee)
            .ShouldBe(SellExclusion.ZeroBalance);
    }

    [Fact]
    public void A_wallet_that_cannot_pay_the_fee_is_excluded()
    {
        // AC16. 0.001 SOL is below the 0.003 floor.
        SellPreflight.Check(tokenRaw: 100UL, solLamports: 1_000_000L, MinSolForFee)
            .ShouldBe(SellExclusion.NotEnoughSolForFee);
    }

    [Fact]
    public void A_wallet_exactly_at_the_fee_floor_is_allowed()
    {
        // The floor is a minimum, not a strict inequality — a wallet funded to exactly the
        // configured amount must not be locked out of its own exit.
        SellPreflight.Check(tokenRaw: 100UL, solLamports: 3_000_000L, MinSolForFee)
            .ShouldBe(SellExclusion.None);
    }

    [Fact]
    public void A_healthy_wallet_passes()
    {
        SellPreflight.Check(tokenRaw: 100UL, solLamports: 1_000_000_000L, MinSolForFee)
            .ShouldBe(SellExclusion.None);
    }

    [Theory]
    [InlineData(5.0, SellExclusion.None)]
    [InlineData(10.0, SellExclusion.None)]        // equal to the cap is allowed
    [InlineData(10.01, SellExclusion.PriceImpactTooHigh)]
    [InlineData(80.0, SellExclusion.PriceImpactTooHigh)]
    public void Price_impact_over_the_cap_aborts_that_wallet(decimal impactPercent, SellExclusion expected)
    {
        // AC18 — and it aborts BEFORE anything is signed or broadcast.
        SellPreflight.CheckPriceImpact(impactPercent, maxPercent: 10m).ShouldBe(expected);
    }

    [Fact]
    public void Every_exclusion_has_an_operator_facing_reason()
    {
        foreach (var value in Enum.GetValues<SellExclusion>())
            SellPreflight.Describe(value).ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void The_low_sol_reason_points_at_the_funder_tool()
    {
        SellPreflight.Describe(SellExclusion.NotEnoughSolForFee)
            .ShouldContain("spacestroker-fund-wallets");
    }
}
