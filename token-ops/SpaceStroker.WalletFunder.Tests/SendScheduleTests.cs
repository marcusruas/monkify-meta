using Shouldly;
using Xunit;

namespace SpaceStroker.WalletFunder.Tests;

public class SendScheduleTests
{
    private static IReadOnlyList<PlannedTransfer> Transfers(int count)
        => Enumerable.Range(1, count)
            .Select(i => new PlannedTransfer($"W{i:00}", $"ADDR{i}", 1m, 1_000_000_000L, Guid.NewGuid()))
            .ToList();

    [Fact]
    public void Schedules_every_transfer_exactly_once()
    {
        var transfers = Transfers(10);

        var schedule = SendSchedule.Build(transfers, TimeSpan.FromMinutes(30), new Random(1));

        schedule.Count.ShouldBe(10);
        schedule.Select(s => s.Transfer.Address).OrderBy(a => a)
            .ShouldBe(transfers.Select(t => t.Address).OrderBy(a => a));
    }

    [Fact]
    public void Offsets_stay_inside_the_window_and_ascend()
    {
        var window = TimeSpan.FromMinutes(30);

        var schedule = SendSchedule.Build(Transfers(10), window, new Random(7));

        schedule.ShouldAllBe(s => s.Offset >= TimeSpan.Zero && s.Offset < window);
        schedule.Select(s => s.Offset).ShouldBe(schedule.Select(s => s.Offset).OrderBy(o => o));
    }

    [Fact]
    public void A_zero_window_sends_back_to_back()
        => SendSchedule.Build(Transfers(5), TimeSpan.Zero, new Random(3))
            .ShouldAllBe(s => s.Offset == TimeSpan.Zero);

    [Fact]
    public void The_same_seed_produces_the_same_schedule()
    {
        var transfers = Transfers(8);

        var a = SendSchedule.Build(transfers, TimeSpan.FromMinutes(20), new Random(42));
        var b = SendSchedule.Build(transfers, TimeSpan.FromMinutes(20), new Random(42));

        a.Select(s => s.Transfer.Address).ShouldBe(b.Select(s => s.Transfer.Address));
        a.Select(s => s.Offset).ShouldBe(b.Select(s => s.Offset));
    }

    [Fact]
    public void Different_seeds_generally_reorder_the_pool()
    {
        var transfers = Transfers(12);

        var a = SendSchedule.Build(transfers, TimeSpan.FromMinutes(20), new Random(1));
        var b = SendSchedule.Build(transfers, TimeSpan.FromMinutes(20), new Random(2));

        a.Select(s => s.Transfer.Address).ShouldNotBe(b.Select(s => s.Transfer.Address));
    }

    [Fact]
    public void An_empty_pool_schedules_nothing()
        => SendSchedule.Build(Array.Empty<PlannedTransfer>(), TimeSpan.FromMinutes(5), new Random(1))
            .ShouldBeEmpty();
}
