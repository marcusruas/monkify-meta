using Shouldly;
using SpaceStroker.TokenSniper.Domain;
using SpaceStroker.TokenSniper.Infrastructure;
using Xunit;

namespace SpaceStroker.TokenSniper.Tests.Infrastructure;

public class ReceiptsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sniper-receipts-{Guid.NewGuid():N}");

    [Fact]
    public void Save_ThenTryLoad_RoundTripsTheRunIncludingStatuses()
    {
        var path = Path.Combine(_directory, "snipe-receipts.json");
        var report = Report();
        report.Wallets[0].Status = WalletStatus.Filled;
        report.Wallets[0].Signature = "3bSgjdQzi9U58csSkaXUC1WNpa6E8vaWv4Yty5jZvwQ";

        new ReceiptsStore(path, report).Save();
        var reloaded = ReceiptsStore.TryLoad(path);

        reloaded.ShouldNotBeNull();
        reloaded!.Wallets[0].Status.ShouldBe(WalletStatus.Filled);
        reloaded.Wallets[0].Signature.ShouldBe("3bSgjdQzi9U58csSkaXUC1WNpa6E8vaWv4Yty5jZvwQ");
        reloaded.Mint.ShouldBe(report.Mint);
    }

    [Fact]
    public void Save_CreatesTheDirectoryItWritesInto()
    {
        var path = Path.Combine(_directory, "nested", "snipe-receipts.json");

        new ReceiptsStore(path, Report()).Save();

        File.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public void TryLoad_ReturnsNullWhenThereIsNoFile()
    {
        ReceiptsStore.TryLoad(Path.Combine(_directory, "absent.json")).ShouldBeNull();
    }

    [Fact]
    public void TryLoad_ReportsCorruptionInsteadOfSwallowingIt()
    {
        var path = Path.Combine(_directory, "corrupt.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "{ not json");

        Should.Throw<SniperException>(() => ReceiptsStore.TryLoad(path))
            .Message.ShouldContain("on-chain balances");
    }

    [Fact]
    public void SavedReceipts_NeverContainAPrivateKeyField()
    {
        var path = Path.Combine(_directory, "snipe-receipts.json");

        new ReceiptsStore(path, Report()).Save();

        File.ReadAllText(path).ShouldNotContain("PrivateKey", Case.Insensitive);
    }

    private static RunReport Report()
    {
        var report = new RunReport
        {
            Mint = "6RTQtuhu19q4LnCxbJLPEWjS1USoqAbSBLkM93tXpump",
            StartedUtc = DateTimeOffset.UnixEpoch,
            MintTotalSupplyBaseUnits = 1_000_000_000_000_000,
        };

        report.Wallets.Add(new WalletOutcome { Alias = "W01", PublicKey = "addr-W01", TargetPercent = 0.4504m });
        return report;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
