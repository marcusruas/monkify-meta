using Shouldly;
using SpaceStroker.TokenSniper.Domain;
using Xunit;

namespace SpaceStroker.TokenSniper.Tests.Domain;

public class RunLedgerTests
{
    [Fact]
    public void Recalculate_DerivesAchievedPercentPerWalletAndForThePool()
    {
        var ledger = new RunLedger(Report(
            Wallet("W01", held: 4_504_000_000_000, received: 4_504_000_000_000, spent: 1_000_000_000),
            Wallet("W02", held: 2_252_000_000_000, received: 2_252_000_000_000, spent: 600_000_000)));

        ledger.Recalculate();

        ledger.Report.Wallets[0].AchievedPercent.ShouldBe(0.4504m);
        ledger.Report.Wallets[1].AchievedPercent.ShouldBe(0.2252m);
        ledger.Report.PoolPercentOfSupply.ShouldBe(0.6756m);
        ledger.Report.TotalSolSpentLamports.ShouldBe(1_600_000_000ul);
        ledger.Report.TotalTokensReceivedBaseUnits.ShouldBe(6_756_000_000_000ul);
    }

    [Fact]
    public void Recalculate_LeavesPricesAtZeroWhenNothingWasBought()
    {
        var ledger = new RunLedger(Report(Wallet("W01", held: 0, received: 0, spent: 0)));

        ledger.Recalculate();

        ledger.Report.Wallets[0].AveragePriceLamportsPerToken.ShouldBe(0m);
        ledger.Report.BlendedAveragePriceLamportsPerToken.ShouldBe(0m);
    }

    [Fact]
    public void ExitCode_IsZeroWhenEveryWalletFilledOrWasAlreadyAtTarget()
    {
        var ledger = new RunLedger(Report(
            Wallet("W01", 1, 1, 1, WalletStatus.Filled),
            Wallet("W02", 1, 0, 0, WalletStatus.Skipped)));

        ledger.ExitCode.ShouldBe(0);
    }

    [Theory]
    [InlineData(WalletStatus.Short)]
    [InlineData(WalletStatus.Failed)]
    [InlineData(WalletStatus.Unconfirmed)]
    [InlineData(WalletStatus.Pending)]
    public void ExitCode_IsNonZeroForAnythingUnresolved(WalletStatus status)
    {
        var ledger = new RunLedger(Report(
            Wallet("W01", 1, 1, 1, WalletStatus.Filled),
            Wallet("W02", 1, 0, 0, status)));

        ledger.ExitCode.ShouldBe(1);
    }

    [Fact]
    public void ExitCode_IsNonZeroWhenTheRunStoppedEarlyEvenIfEveryTouchedWalletFilled()
    {
        var report = Report(Wallet("W01", 1, 1, 1, WalletStatus.Filled));
        report.StoppedReason = "the token graduated";

        new RunLedger(report).ExitCode.ShouldBe(1);
    }

    [Fact]
    public void ExitCode_IsZeroForADryRunOrAnEmptyPool()
    {
        var dry = Report(Wallet("W01", 1, 0, 0, WalletStatus.Pending));
        dry.DryRun = true;
        new RunLedger(dry).ExitCode.ShouldBe(0);

        new RunLedger(Report()).ExitCode.ShouldBe(0);
    }

    [Fact]
    public void For_ThrowsOnAnUnknownAlias()
    {
        Should.Throw<SniperException>(() => new RunLedger(Report()).For("nope"));
    }

    private static RunReport Report(params WalletOutcome[] wallets)
    {
        var report = new RunReport
        {
            Mint = "6RTQtuhu19q4LnCxbJLPEWjS1USoqAbSBLkM93tXpump",
            MintTotalSupplyBaseUnits = 1_000_000_000_000_000,
            StartedUtc = DateTimeOffset.UnixEpoch,
        };

        report.Wallets.AddRange(wallets);
        return report;
    }

    private static WalletOutcome Wallet(
        string alias, ulong held, ulong received, ulong spent, WalletStatus status = WalletStatus.Filled) =>
        new()
        {
            Alias = alias,
            PublicKey = $"addr-{alias}",
            HeldAfterBaseUnits = held,
            TokensReceivedBaseUnits = received,
            SolSpentLamports = spent,
            Status = status,
        };
}
