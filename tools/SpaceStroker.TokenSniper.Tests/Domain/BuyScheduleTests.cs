using Shouldly;
using SpaceStroker.TokenSniper.Domain;
using Xunit;

namespace SpaceStroker.TokenSniper.Tests.Domain;

public class BuyScheduleTests
{
    [Fact]
    public void Build_OrdersByAliasAndNeverShuffles()
    {
        // ⚠️ The one deliberate difference from the funder's SendSchedule, which DOES shuffle.
        var buys = new[] { Buy("W03"), Buy("W01"), Buy("W02") };

        var schedule = BuySchedule.Build(buys, TimeSpan.FromMinutes(60), new Random(1234));

        schedule.Select(s => s.Buy.Target.Alias).ShouldBe(["W01", "W02", "W03"]);
    }

    [Fact]
    public void Build_GivesAscendingOffsetsInsideTheWindow()
    {
        var buys = Enumerable.Range(1, 12).Select(i => Buy($"W{i:00}")).ToArray();
        var window = TimeSpan.FromMinutes(60);

        var schedule = BuySchedule.Build(buys, window, new Random(7));

        schedule.Select(s => s.Offset).ShouldBeInOrder();
        schedule.Last().Offset.ShouldBeLessThan(window);
        schedule.First().Offset.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public void Build_WithAZeroWindowRunsBackToBack()
    {
        var schedule = BuySchedule.Build([Buy("W01"), Buy("W02")], TimeSpan.Zero, new Random(1));

        schedule.ShouldAllBe(s => s.Offset == TimeSpan.Zero);
    }

    [Fact]
    public void Build_IsReproducibleForASeededRandom()
    {
        var buys = new[] { Buy("W01"), Buy("W02"), Buy("W03") };

        var first = BuySchedule.Build(buys, TimeSpan.FromMinutes(60), new Random(42)).Select(s => s.Offset);
        var second = BuySchedule.Build(buys, TimeSpan.FromMinutes(60), new Random(42)).Select(s => s.Offset);

        first.ShouldBe(second);
    }

    [Fact]
    public void Build_HandlesAnEmptyPool()
    {
        BuySchedule.Build([], TimeSpan.FromMinutes(60), new Random(1)).ShouldBeEmpty();
    }

    [Fact]
    public void MixesPaddedAndUnpaddedAliases_DetectsTheOrdinalSortingTrap()
    {
        // "W10" sorts before "W2" ordinally — a warning, not an error.
        BuySchedule.MixesPaddedAndUnpaddedAliases(["W2", "W10"]).ShouldBeTrue();
        BuySchedule.MixesPaddedAndUnpaddedAliases(["W02", "W10"]).ShouldBeFalse();
        BuySchedule.MixesPaddedAndUnpaddedAliases(["alpha", "beta"]).ShouldBeFalse();
        BuySchedule.MixesPaddedAndUnpaddedAliases(["W01"]).ShouldBeFalse();
    }

    private static PlannedBuy Buy(string alias) =>
        new(new WalletTarget(alias, $"addr-{alias}", 1m, 1_000, 0), 1_000, 500, 625, false);
}
