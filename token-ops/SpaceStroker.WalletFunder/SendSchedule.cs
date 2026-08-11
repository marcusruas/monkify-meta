namespace SpaceStroker.WalletFunder;

public sealed record ScheduledTransfer(PlannedTransfer Transfer, TimeSpan Offset);

/// <summary>
/// Spreads the batch over a window in randomized order with randomized gaps.
/// A Coinbase send leaves Coinbase's shared hot wallets, so the SENDER does not identify the account —
/// CLUSTERING does. Twelve deposits in the same minute that then all buy the same token is the tell.
/// `Random` is injected so the schedule is unit-testable; production passes Random.Shared.
/// </summary>
public static class SendSchedule
{
    public static IReadOnlyList<ScheduledTransfer> Build(
        IReadOnlyList<PlannedTransfer> transfers, TimeSpan window, Random rng)
    {
        if (transfers.Count == 0) return Array.Empty<ScheduledTransfer>();

        var order = transfers.ToArray();
        for (var i = order.Length - 1; i > 0; i--)                 // Fisher-Yates
        {
            var j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var offsets = new TimeSpan[order.Length];
        if (window > TimeSpan.Zero)
        {
            var ticks = Enumerable.Range(0, order.Length)
                .Select(_ => (long)(rng.NextDouble() * window.Ticks))
                .OrderBy(t => t)
                .ToArray();

            for (var i = 0; i < order.Length; i++) offsets[i] = TimeSpan.FromTicks(ticks[i]);
        }

        return order.Select((transfer, i) => new ScheduledTransfer(transfer, offsets[i])).ToList();
    }
}
