using Shouldly;
using SpaceStroker.TokenSniper.Domain;
using Xunit;

namespace SpaceStroker.TokenSniper.Tests.Domain;

public class SnipePoolTests
{
    [Fact]
    public void Parse_ReadsTheSniperFieldsAndIgnoresTheFundersOwn()
    {
        // The pool file is SHARED with spacestroker-fund-wallets: Label and SOLAmount belong to it and
        // must not break parsing here (AC18), exactly as this tool's fields do not break it there.
        var json = """
            [
              {
                "alias": "W01",
                "Label": "W01",
                "PublicKey": "7Q6RcQKsmpaRsaK17nuVyxDEWMRcPJUhboHvsqNRgTsR",
                "PrivateKey": "secret",
                "SOLAmount": 2.54,
                "SupplyPercentage": 0.4504
              }
            ]
            """;

        var pool = SnipePool.Parse(json);

        pool.Count.ShouldBe(1);
        pool[0].Alias.ShouldBe("W01");
        pool[0].PublicKey.ShouldBe("7Q6RcQKsmpaRsaK17nuVyxDEWMRcPJUhboHvsqNRgTsR");
        pool[0].PrivateKey.ShouldBe("secret");
        pool[0].SupplyPercentage.ShouldBe(0.4504m);
    }

    [Fact]
    public void Parse_KeepsABlankAliasBlankInsteadOfInventingOne()
    {
        // ⚠️ The funder's WalletPool synthesises "W01" for a blank Label. Doing that here would
        // silently reorder the run, because the alias IS the execution order — preflight rejects it.
        var pool = SnipePool.Parse("""[{ "PublicKey": "addr", "SupplyPercentage": 1 }]""");

        pool[0].Alias.ShouldBe("");
    }

    [Fact]
    public void Parse_TrimsWhitespaceAroundTheValuesThatBecomeKeys()
    {
        var pool = SnipePool.Parse("""[{ "alias": "  W01  ", "PublicKey": " addr ", "PrivateKey": " k ", "SupplyPercentage": 1 }]""");

        pool[0].Alias.ShouldBe("W01");
        pool[0].PublicKey.ShouldBe("addr");
        pool[0].PrivateKey.ShouldBe("k");
    }

    [Fact]
    public void Parse_AllowsCommentsAndTrailingCommas()
    {
        var json = """
            [
              // the first ghost
              { "alias": "W01", "PublicKey": "addr", "PrivateKey": "k", "SupplyPercentage": 1, },
            ]
            """;

        SnipePool.Parse(json).Count.ShouldBe(1);
    }

    [Fact]
    public void Parse_TurnsMalformedJsonIntoAnActionableError()
    {
        Should.Throw<SniperException>(() => SnipePool.Parse("{ not json"))
            .Message.ShouldContain("Could not parse the wallet pool");
    }

    [Fact]
    public void Parse_HandlesAnEmptyArray()
    {
        SnipePool.Parse("[]").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0ul, "0")]
    [InlineData(1_500_000ul, "1.5")]
    [InlineData(4_504_000_000_000ul, "4504000")]
    public void FormatTokens_IsInvariant(ulong baseUnits, string expected)
    {
        SnipePool.FormatTokens(baseUnits, 6).ShouldBe(expected);
    }

    [Fact]
    public void FormatSol_IsInvariantAndFullPrecision()
    {
        SnipePool.FormatSol(1_234_567_890).ShouldBe("1.234567890");
    }
}
