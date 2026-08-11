using System.Globalization;
using System.Text;
using SpaceStroker.TokenSniper.Domain;

namespace SpaceStroker.TokenSniper.Application;

/// <summary>
/// Everything the operator sees before money moves. Mirrors the funder's ConsoleReport, including its
/// hard-won rule: ⚠️ every number is formatted with <see cref="CultureInfo.InvariantCulture"/>, because
/// a pt-BR machine renders 2.54 as "2,54" and the typed confirmation would then never match.
/// </summary>
public static class ConsoleReport
{
    public static string RenderLadder(IReadOnlyList<ScheduledBuy> schedule, int decimals)
    {
        var table = new StringBuilder();
        table.AppendLine();
        table.AppendLine("  ALIAS      TARGET %   HELD           SHORTFALL      QUOTE (SOL)     MAX (SOL)       AT");
        table.AppendLine("  ---------- ---------- -------------- -------------- --------------- --------------- --------");

        foreach (var scheduled in schedule)
        {
            var buy = scheduled.Buy;
            table.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {Pad(buy.Target.Alias, 10)} {Pad(SnipePool.FormatPercent(buy.Target.SupplyPercentage), 10)} " +
                $"{Pad(SnipePool.FormatTokens(buy.Target.HeldBaseUnits, decimals), 14)} " +
                $"{Pad(SnipePool.FormatTokens(buy.Target.ShortfallBaseUnits, decimals), 14)} " +
                $"{Pad(SnipePool.FormatSol(buy.QuotedCostLamports), 15)} " +
                $"{Pad(SnipePool.FormatSol(buy.MaxSolCostLamports), 15)} " +
                $"{scheduled.Offset:hh\\:mm\\:ss}{Suffix(buy)}"));
        }

        var total = schedule.Aggregate(0ul, (sum, s) => sum + s.Buy.QuotedCostLamports);
        table.AppendLine();
        table.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"  {schedule.Count(s => !s.Buy.IsSkip)} buys, quoted total {SnipePool.FormatSol(total)} SOL " +
            $"({schedule.Count(s => s.Buy.IsSkip)} skipped)"));

        return table.ToString();
    }

    /// <summary>The quoted total in the exact form the operator must type back.</summary>
    public static string ConfirmationTotal(IReadOnlyList<ScheduledBuy> schedule) =>
        SnipePool.FormatSol(schedule.Aggregate(0ul, (sum, s) => sum + s.Buy.QuotedCostLamports));

    /// <summary>
    /// Invariant comparison against the typed value. A comma decimal never passes — that is the point:
    /// if the operator's keyboard/locale produced "12,4", they are not reading the number we quoted.
    /// </summary>
    public static bool ConfirmationMatches(string expectedTotal, string? typed) =>
        typed is not null &&
        string.Equals(typed.Trim(), expectedTotal.Trim(), StringComparison.Ordinal);

    public static string RenderSummary(RunReport report)
    {
        var summary = new StringBuilder();
        summary.AppendLine();
        summary.AppendLine("  ALIAS      STATUS       ACHIEVED %  BOUGHT         SOL SPENT       SIGNATURE");
        summary.AppendLine("  ---------- ------------ ----------- -------------- --------------- ------------");

        foreach (var wallet in report.Wallets)
            summary.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {Pad(wallet.Alias, 10)} {Pad(wallet.Status.ToString(), 12)} " +
                $"{Pad(SnipePool.FormatPercent(wallet.AchievedPercent), 11)} " +
                $"{Pad(SnipePool.FormatTokens(wallet.TokensReceivedBaseUnits, report.TokenDecimals), 14)} " +
                $"{Pad(SnipePool.FormatSol(wallet.SolSpentLamports), 15)} " +
                $"{(wallet.Signature is null ? "-" : wallet.Signature[..Math.Min(12, wallet.Signature.Length)] + "...")}"));

        summary.AppendLine();
        summary.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"  Pool now holds {SnipePool.FormatPercent(report.PoolPercentOfSupply)}% of supply " +
            $"for {SnipePool.FormatSol(report.TotalSolSpentLamports)} SOL."));

        if (!string.IsNullOrWhiteSpace(report.StoppedReason))
            summary.AppendLine($"  !! Run stopped early: {report.StoppedReason}");

        return summary.ToString();
    }

    private static string Suffix(PlannedBuy buy) =>
        buy.IsSkip ? $"   ({buy.SkipReason})" : buy.CappedByBalance ? "   (capped by balance)" : "";

    private static string Pad(string value, int width) =>
        value.Length >= width ? value : value.PadRight(width);
}
