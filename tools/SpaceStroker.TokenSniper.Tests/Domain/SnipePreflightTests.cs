using Shouldly;
using Solnet.Wallet;
using Solnet.Wallet.Bip39;
using SpaceStroker.TokenSniper.Domain;
using Xunit;

namespace SpaceStroker.TokenSniper.Tests.Domain;

public class SnipePreflightTests
{
    private const ulong Supply = 1_000_000_000_000_000;

    [Fact]
    public void Check_AnEmptyPoolIsANoOpNotAnError()
    {
        var result = SnipePreflight.Check([], Curve(), Supply);

        result.IsEmpty.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Check_AcceptsAWellFormedPool()
    {
        var result = SnipePreflight.Check([Entry("W01"), Entry("W02")], Curve(), Supply);

        result.Errors.ShouldBeEmpty();
        result.Ok.ShouldBeTrue();
    }

    [Fact]
    public void Check_ReportsEveryProblemAtOnceRatherThanTheFirst()
    {
        var pool = new[]
        {
            Entry("") with { PublicKey = "not-an-address" },
            Entry("W02") with { SupplyPercentage = 0 },
        };

        var result = SnipePreflight.Check(pool, Curve(), Supply);

        result.Errors.Count.ShouldBeGreaterThanOrEqualTo(3);
        result.Errors.ShouldContain(e => e.Contains("alias is required"));
        result.Errors.ShouldContain(e => e.Contains("not a valid Solana address"));
        result.Errors.ShouldContain(e => e.Contains("SupplyPercentage"));
    }

    [Fact]
    public void Check_RejectsADuplicateAliasBecauseItIsTheOrderingKey()
    {
        var result = SnipePreflight.Check([Entry("W01"), Entry("W01")], Curve(), Supply);

        result.Errors.ShouldContain(e => e.Contains("Duplicate alias"));
    }

    [Fact]
    public void Check_RejectsADuplicateAddress()
    {
        var shared = Entry("W01");
        var result = SnipePreflight.Check([shared, Entry("W02") with { PublicKey = shared.PublicKey }], Curve(), Supply);

        result.Errors.ShouldContain(e => e.Contains("Duplicate address"));
    }

    [Fact]
    public void Check_RejectsAKeypairWhoseHalvesCameFromDifferentWallets()
    {
        var mismatched = Entry("W01") with { PublicKey = Entry("W02").PublicKey };

        var result = SnipePreflight.Check([mismatched], Curve(), Supply);

        result.Errors.ShouldContain(e => e.Contains("does not derive the PublicKey"));
    }

    [Fact]
    public void Check_RejectsAMissingPrivateKey()
    {
        var result = SnipePreflight.Check([Entry("W01") with { PrivateKey = "" }], Curve(), Supply);

        result.Errors.ShouldContain(e => e.Contains("PrivateKey is required"));
    }

    [Fact]
    public void Check_RefusesAGraduatedCurve()
    {
        var result = SnipePreflight.Check([Entry("W01")], Curve() with { Complete = true }, Supply);

        result.Errors.ShouldContain(e => e.Contains("graduated"));
    }

    [Fact]
    public void Check_RefusesAMissingCurveAccount()
    {
        var result = SnipePreflight.Check([Entry("W01")], null, Supply);

        result.Errors.ShouldContain(e => e.Contains("does not exist"));
    }

    [Fact]
    public void Check_RefusesACurveQuotedInSomethingOtherThanSol()
    {
        var usdcQuoted = Curve() with { QuoteMint = "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v" };

        var result = SnipePreflight.Check([Entry("W01")], usdcQuoted, Supply);

        result.Errors.ShouldContain(e => e.Contains("not SOL"));
    }

    [Fact]
    public void Check_RefusesTargetsBeyondWhatTheCurveCanStillSell()
    {
        var greedy = Entry("W01") with { SupplyPercentage = 90m };

        var result = SnipePreflight.Check([greedy], Curve(), Supply);

        result.Errors.ShouldContain(e => e.Contains("can only sell"));
    }

    [Fact]
    public void Check_WarnsButDoesNotFailOnMixedAliasPadding()
    {
        var result = SnipePreflight.Check([Entry("W2"), Entry("W10")], Curve(), Supply);

        result.Errors.ShouldBeEmpty();
        result.Warnings.ShouldContain(w => w.Contains("zero-padded"));
    }

    [Fact]
    public void Check_RejectsAMintReportingNoSupply()
    {
        var result = SnipePreflight.Check([Entry("W01")], Curve(), 0);

        result.Errors.ShouldContain(e => e.Contains("total supply of zero"));
    }

    [Fact]
    public void IsValidSolanaAddress_RejectsBlankGarbageAndTruncation()
    {
        SnipePreflight.IsValidSolanaAddress("").ShouldBeFalse();
        SnipePreflight.IsValidSolanaAddress("not-an-address").ShouldBeFalse();
        SnipePreflight.IsValidSolanaAddress("7Q6RcQKsmpaRsaK17nuVyxDEWMRcPJUhboHvsqNRgTsR").ShouldBeTrue();

        // ⚠️ A pasted-short address decodes to FEWER than 32 bytes, which is why the length assertion
        // exists at all — PublicKey.IsValid alone lets some of these through.
        SnipePreflight.IsValidSolanaAddress("7Q6RcQKsmpaRsaK17nuVyxDEWMRcPJUhbo").ShouldBeFalse();
        SnipePreflight.IsValidSolanaAddress("7Q6RcQ").ShouldBeFalse();
    }

    [Fact]
    public void KeyPairMatches_OnlyForATrueKeypair()
    {
        var wallet = new Wallet(WordCount.Twelve, WordList.English);
        var other = new Wallet(WordCount.Twelve, WordList.English);

        SnipePreflight.KeyPairMatches(wallet.Account.PrivateKey.Key, wallet.Account.PublicKey.Key).ShouldBeTrue();
        SnipePreflight.KeyPairMatches(wallet.Account.PrivateKey.Key, other.Account.PublicKey.Key).ShouldBeFalse();
        SnipePreflight.KeyPairMatches("garbage", wallet.Account.PublicKey.Key).ShouldBeFalse();
    }

    private static PoolEntry Entry(string alias)
    {
        var wallet = new Wallet(WordCount.Twelve, WordList.English);
        return new PoolEntry(alias, wallet.Account.PublicKey.Key, wallet.Account.PrivateKey.Key, 0.4504m);
    }

    private static CurveState Curve() =>
        new(587_705_864_083_302, 54_772_298_209, 307_805_864_083_302, 24_772_298_209,
            Supply, false, "69uK17vg5VqGQ7xVRRRhGgGeddhvXiTKREmbnXRDaZLV", false, false, BondingCurve.SolQuoteMint);
}
