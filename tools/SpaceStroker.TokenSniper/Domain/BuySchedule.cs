namespace SpaceStroker.TokenSniper.Domain;

public sealed record ScheduledBuy(PlannedBuy Buy, TimeSpan Offset);

/// <summary>
/// Spreads the pool's buys across the window.
///
/// ⚠️ The one deliberate difference from the funder's <c>SendSchedule</c>: the ORDER IS NOT SHUFFLED.
/// The card fixes execution order to ascending alias (a change from its original "largest supply
/// first"), so only the gaps jitter. <c>Random</c> is injected so the schedule is unit-testable;
/// production passes <c>Random.Shared</c>.
/// </summary>
public static class BuySchedule
{
    public static IReadOnlyList<ScheduledBuy> Build(
        IReadOnlyList<PlannedBuy> buys, TimeSpan window, Random rng)
    {
        if (buys.Count == 0) return Array.Empty<ScheduledBuy>();

        var ordered = buys.OrderBy(b => b.Target.Alias, StringComparer.Ordinal).ToArray();

        var offsets = new TimeSpan[ordered.Length];
        if (window > TimeSpan.Zero)
        {
            var ticks = Enumerable.Range(0, ordered.Length)
                .Select(_ => (long)(rng.NextDouble() * window.Ticks))
                .OrderBy(t => t)
                .ToArray();

            for (var i = 0; i < ordered.Length; i++) offsets[i] = TimeSpan.FromTicks(ticks[i]);
        }

        return ordered.Select((buy, i) => new ScheduledBuy(buy, offsets[i])).ToList();
    }

    /// <summary>
    /// Ordinal sorting puts "W10" before "W2". Callers surface this as a preflight WARNING rather than
    /// silently reordering the operator's intent.
    /// </summary>
    public static bool MixesPaddedAndUnpaddedAliases(IEnumerable<string> aliases)
    {
        var numericTails = aliases
            .Select(alias => new string(alias.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray()))
            .Where(tail => tail.Length > 0)
            .ToList();

        if (numericTails.Count < 2) return false;

        return numericTails.Select(tail => tail.Length).Distinct().Count() > 1;
    }
}
