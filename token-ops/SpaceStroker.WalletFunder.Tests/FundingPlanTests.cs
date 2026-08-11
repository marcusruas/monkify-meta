using Shouldly;
using Xunit;

namespace SpaceStroker.WalletFunder.Tests;

public class FundingPlanTests
{
    private static IReadOnlyList<PoolEntry> Pool(params (string addr, decimal sol)[] items)
        => items.Select((x, i) => new PoolEntry($"W{i + 1:00}", x.addr, x.sol)).ToList();

    [Fact]
    public void Same_pool_produces_identical_idem_tokens()
    {
        var a = FundingPlan.Build(Pool(("AAA", 2.54m), ("BBB", 1m)));
        var b = FundingPlan.Build(Pool(("AAA", 2.54m), ("BBB", 1m)));

        a.PlanHash.ShouldBe(b.PlanHash);
        a.Transfers.Select(t => t.Idem).ShouldBe(b.Transfers.Select(t => t.Idem));
    }

    [Fact]
    public void Reordering_the_pool_does_not_change_the_plan_hash()
        => FundingPlan.Build(Pool(("AAA", 1m), ("BBB", 2m))).PlanHash
            .ShouldBe(FundingPlan.Build(Pool(("BBB", 2m), ("AAA", 1m))).PlanHash);

    [Fact]
    public void Changing_an_amount_changes_that_wallets_idem_and_the_plan_hash()
    {
        var before = FundingPlan.Build(Pool(("AAA", 1m), ("BBB", 2m)));
        var after = FundingPlan.Build(Pool(("AAA", 1m), ("BBB", 3m)));

        after.PlanHash.ShouldNotBe(before.PlanHash);
        after.Transfers.Single(t => t.Address == "BBB").Idem
            .ShouldNotBe(before.Transfers.Single(t => t.Address == "BBB").Idem);
    }

    [Fact]
    public void Changing_an_address_changes_the_plan_hash()
        => FundingPlan.Build(Pool(("AAA", 1m))).PlanHash
            .ShouldNotBe(FundingPlan.Build(Pool(("ZZZ", 1m))).PlanHash);

    [Fact]
    public void Renaming_a_label_does_NOT_change_the_plan_hash_or_idem()
    {
        var a = FundingPlan.Build(new List<PoolEntry> { new("W01", "AAA", 1m) });
        var b = FundingPlan.Build(new List<PoolEntry> { new("RENAMED", "AAA", 1m) });

        b.PlanHash.ShouldBe(a.PlanHash);
        b.Transfers[0].Idem.ShouldBe(a.Transfers[0].Idem);
    }

    [Fact]
    public void Idem_is_shaped_like_a_uuid_v4()
    {
        var idem = FundingPlan.Build(Pool(("AAA", 1m))).Transfers[0].Idem;

        // The canonical UUID string is what Coinbase sees: 8-4-4-4-12 with '4' opening the third
        // group and the variant nibble (8, 9, a or b) opening the fourth.
        var text = idem.ToString("D");
        var groups = text.Split('-');
        groups.Length.ShouldBe(5);
        groups[2][0].ShouldBe('4');
        "89ab".ShouldContain(groups[3][0]);
    }

    [Fact]
    public void Total_is_the_sum_of_the_pool()
        => FundingPlan.Build(Pool(("AAA", 2.54m), ("BBB", 1.5m))).TotalSol.ShouldBe(4.04m);

    [Fact]
    public void Preserves_the_authored_order_of_the_pool()
        => FundingPlan.Build(Pool(("ZZZ", 1m), ("AAA", 1m))).Transfers.Select(t => t.Address)
            .ShouldBe(new[] { "ZZZ", "AAA" });
}
