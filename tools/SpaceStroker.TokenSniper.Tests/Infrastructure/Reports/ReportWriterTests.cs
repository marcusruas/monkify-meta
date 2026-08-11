using System.Text.Json;
using Shouldly;
using SpaceStroker.TokenSniper.Domain;
using SpaceStroker.TokenSniper.Infrastructure.Reports;
using Xunit;

namespace SpaceStroker.TokenSniper.Tests.Infrastructure.Reports;

/// <summary>
/// Both artifacts are produced independently (the card's decision), so both are asserted independently
/// against the same ledger — including the rule that neither may ever carry key material.
/// </summary>
public class ReportWriterTests : IDisposable
{
    private const string PrivateKeyThatMustNeverAppear = "4NMwxzmYj2uvHuq8xoqhY8RXg63KSVJM1DXkpbmkUY7Y";
    private const string RpcKeyThatMustNeverAppear = "helius-key-abcdef123456";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sniper-reports-{Guid.NewGuid():N}");

    [Fact]
    public void JsonReport_CarriesEveryPerWalletFieldAndTheRunTotals()
    {
        var path = JsonReportWriter.Write(Report(), _directory);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        root.GetProperty("mint").GetString().ShouldBe("6RTQtuhu19q4LnCxbJLPEWjS1USoqAbSBLkM93tXpump");
        root.GetProperty("poolPercentOfSupply").GetDecimal().ShouldBe(0.6756m);
        root.GetProperty("totalSolSpentLamports").GetUInt64().ShouldBe(1_600_000_000ul);
        root.GetProperty("blendedAveragePriceLamportsPerToken").GetDecimal().ShouldBeGreaterThan(0m);

        var wallet = root.GetProperty("wallets")[0];
        foreach (var field in new[]
                 {
                     "alias", "publicKey", "targetPercent", "achievedPercent", "targetBaseUnits",
                     "tokensReceivedBaseUnits", "solSpentLamports", "averagePriceLamportsPerToken",
                     "signature", "status", "note", "scheduledOffsetSeconds",
                 })
            wallet.TryGetProperty(field, out _).ShouldBeTrue($"the JSON report is missing '{field}'");

        wallet.GetProperty("status").GetString().ShouldBe("Filled");   // enum as name, not an int
    }

    [Fact]
    public void HtmlReport_ShowsEveryWalletTheTotalsAndTheShortfallReasons()
    {
        var path = HtmlReportWriter.Write(Report(), _directory);
        var html = File.ReadAllText(path);

        html.ShouldContain("W01");
        html.ShouldContain("W02");
        html.ShouldContain("addr-W01");
        html.ShouldContain("0.6756");                       // pool share of supply
        html.ShouldContain("Wallets that did not reach target");
        html.ShouldContain("bought the maximum the wallet&#39;s SOL allowed");   // HTML-encoded apostrophe
        html.ShouldContain("Short");
    }

    [Fact]
    public void HtmlReport_IsSelfContained()
    {
        // No external CSS/JS/font/image — it has to open from disk on a machine with no network.
        var html = File.ReadAllText(HtmlReportWriter.Write(Report(), _directory));

        html.ShouldNotContain("<script");
        html.ShouldNotContain("<link");
        html.ShouldNotContain("src=");
        html.ShouldNotContain("@import");

        // The only external URLs are explorer anchors the operator clicks deliberately.
        foreach (var fragment in html.Split("http").Skip(1))
            fragment.ShouldStartWith("s://solscan.io/");
    }

    [Fact]
    public void HtmlReport_MarksARunThatStoppedEarly()
    {
        var report = Report();
        report.StoppedReason = "the token graduated after W01";

        File.ReadAllText(HtmlReportWriter.Write(report, _directory))
            .ShouldContain("the token graduated after W01");
    }

    [Fact]
    public void NeitherArtifactEverCarriesKeyMaterial()
    {
        var report = Report();

        var json = File.ReadAllText(JsonReportWriter.Write(report, _directory));
        var html = File.ReadAllText(HtmlReportWriter.Write(report, _directory));

        foreach (var artifact in new[] { json, html })
        {
            artifact.ShouldNotContain(PrivateKeyThatMustNeverAppear);
            artifact.ShouldNotContain(RpcKeyThatMustNeverAppear);
            artifact.ShouldNotContain("PrivateKey", Case.Insensitive);
            artifact.ShouldNotContain("api-key", Case.Insensitive);
        }
    }

    [Fact]
    public void HtmlReport_EscapesWhatItRenders()
    {
        var report = Report();
        report.Wallets[0].Note = "<script>alert('x')</script>";

        File.ReadAllText(HtmlReportWriter.Write(report, _directory))
            .ShouldContain("&lt;script&gt;");
    }

    private static RunReport Report()
    {
        var report = new RunReport
        {
            Mint = "6RTQtuhu19q4LnCxbJLPEWjS1USoqAbSBLkM93tXpump",
            StartedUtc = DateTimeOffset.UnixEpoch,
            FinishedUtc = DateTimeOffset.UnixEpoch.AddHours(1),
            MintTotalSupplyBaseUnits = 1_000_000_000_000_000,
            TokenDecimals = 6,
        };

        report.Wallets.Add(new WalletOutcome
        {
            Alias = "W01",
            PublicKey = "addr-W01",
            TargetPercent = 0.4504m,
            TargetBaseUnits = 4_504_000_000_000,
            TokensReceivedBaseUnits = 4_504_000_000_000,
            HeldAfterBaseUnits = 4_504_000_000_000,
            SolSpentLamports = 1_000_000_000,
            Signature = "2kHXxyAqoiZfy3ucR8TaM7PSPms2HuBpHTagiAWBmCSC",
            Status = WalletStatus.Filled,
            ScheduledOffsetSeconds = 120,
        });

        report.Wallets.Add(new WalletOutcome
        {
            Alias = "W02",
            PublicKey = "addr-W02",
            TargetPercent = 0.4504m,
            TargetBaseUnits = 4_504_000_000_000,
            TokensReceivedBaseUnits = 2_252_000_000_000,
            HeldAfterBaseUnits = 2_252_000_000_000,
            SolSpentLamports = 600_000_000,
            Status = WalletStatus.Short,
            Note = "bought the maximum the wallet's SOL allowed",
            ScheduledOffsetSeconds = 1_500,
        });

        new RunLedger(report).Recalculate();
        return report;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
