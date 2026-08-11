using System.Globalization;
using Shouldly;
using SpaceStroker.TokenSniper.Application;
using SpaceStroker.TokenSniper.Domain;
using Xunit;

namespace SpaceStroker.TokenSniper.Tests.Application;

public class ConsoleReportTests
{
    [Fact]
    public void RenderLadder_ShowsEveryColumnTheOperatorNeedsBeforeApproving()
    {
        var ladder = ConsoleReport.RenderLadder(Schedule(), 6);

        ladder.ShouldContain("ALIAS");
        ladder.ShouldContain("W01");
        ladder.ShouldContain("W02");
        ladder.ShouldContain("0.4504");                 // target %
        ladder.ShouldContain("1.000000000");            // quoted SOL
        ladder.ShouldContain("1.250000000");            // maxSolCost
        ladder.ShouldContain("00:02:00");               // scheduled offset
        ladder.ShouldContain("2 buys, quoted total");
    }

    [Fact]
    public void RenderLadder_LabelsSkippedAndCappedWallets()
    {
        var schedule = new List<ScheduledBuy>
        {
            new(new PlannedBuy(Target("W01"), 0, 0, 0, false, "already at target"), TimeSpan.Zero),
            new(new PlannedBuy(Target("W02"), 10, 5, 6, true), TimeSpan.Zero),
        };

        var ladder = ConsoleReport.RenderLadder(schedule, 6);

        ladder.ShouldContain("(already at target)");
        ladder.ShouldContain("(capped by balance)");
        ladder.ShouldContain("1 buys, quoted total");
        ladder.ShouldContain("(1 skipped)");
    }

    [Fact]
    public void ConfirmationTotal_IsTheSumOfTheQuotesInInvariantForm()
    {
        ConsoleReport.ConfirmationTotal(Schedule()).ShouldBe("2.000000000");
    }

    [Fact]
    public void ConfirmationMatches_AcceptsOnlyTheExactQuotedString()
    {
        ConsoleReport.ConfirmationMatches("2.000000000", "2.000000000").ShouldBeTrue();
        ConsoleReport.ConfirmationMatches("2.000000000", "  2.000000000  ").ShouldBeTrue();

        ConsoleReport.ConfirmationMatches("2.000000000", "2").ShouldBeFalse();
        ConsoleReport.ConfirmationMatches("2.000000000", "2.0").ShouldBeFalse();
        ConsoleReport.ConfirmationMatches("2.000000000", null).ShouldBeFalse();
    }

    [Fact]
    public void ConfirmationMatches_RejectsACommaDecimal()
    {
        // ⚠️ The funder's hard-won lesson: a pt-BR machine renders 2.0 as "2,0". If the operator types
        // that, they are not reading the number we quoted — refuse rather than guess.
        ConsoleReport.ConfirmationMatches("2.000000000", "2,000000000").ShouldBeFalse();
    }

    [Fact]
    public void NumbersAreInvariantEvenUnderAptBrCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");

            ConsoleReport.ConfirmationTotal(Schedule()).ShouldBe("2.000000000");
            ConsoleReport.RenderLadder(Schedule(), 6).ShouldNotContain(",000000000");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void RenderSummary_ReportsStatusAchievedShareAndAnEarlyStop()
    {
        var report = new RunReport
        {
            Mint = "mint",
            MintTotalSupplyBaseUnits = 1_000_000_000_000_000,
            StoppedReason = "the token graduated after W01",
        };

        report.Wallets.Add(new WalletOutcome
        {
            Alias = "W01",
            Status = WalletStatus.Filled,
            HeldAfterBaseUnits = 4_504_000_000_000,
            TokensReceivedBaseUnits = 4_504_000_000_000,
            SolSpentLamports = 1_000_000_000,
        });

        new RunLedger(report).Recalculate();
        var summary = ConsoleReport.RenderSummary(report);

        summary.ShouldContain("W01");
        summary.ShouldContain("Filled");
        summary.ShouldContain("0.4504");
        summary.ShouldContain("Pool now holds");
        summary.ShouldContain("Run stopped early: the token graduated after W01");
    }

    private static List<ScheduledBuy> Schedule() =>
    [
        new(new PlannedBuy(Target("W01"), 1_000, 1_000_000_000, 1_250_000_000, false), TimeSpan.FromMinutes(2)),
        new(new PlannedBuy(Target("W02"), 1_000, 1_000_000_000, 1_250_000_000, false), TimeSpan.FromMinutes(25)),
    ];

    private static WalletTarget Target(string alias) =>
        new(alias, $"addr-{alias}", 0.4504m, 4_504_000_000_000, 0);
}
