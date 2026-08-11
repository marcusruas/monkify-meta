using System.Globalization;
using Shouldly;
using Xunit;

namespace SpaceStroker.WalletFunder.Tests;

public class WalletPoolTests
{
    [Fact]
    public void Ignores_unknown_fields_including_PrivateKey()
    {
        var json = """
        [ { "PublicKey": "AAA", "PrivateKey": "SUPER_SECRET", "SupplyPercentage": 0.45, "SOLAmount": 2.54 } ]
        """;

        var pool = WalletPool.Parse(json);

        pool.Count.ShouldBe(1);
        pool[0].PublicKey.ShouldBe("AAA");
        pool[0].SolAmount.ShouldBe(2.54m);
        // The type has no PrivateKey member at all — a spend key can never be bound.
        typeof(PoolEntry).GetProperty("PrivateKey").ShouldBeNull();
    }

    [Fact]
    public void Defaults_the_label_by_index_when_absent()
    {
        var json = """[ { "PublicKey": "A", "SOLAmount": 1 }, { "PublicKey": "B", "SOLAmount": 2 } ]""";

        var pool = WalletPool.Parse(json);

        pool[0].Label.ShouldBe("W01");
        pool[1].Label.ShouldBe("W02");
    }

    [Fact]
    public void Keeps_an_explicit_label()
        => WalletPool.Parse("""[ { "Label": "W10", "PublicKey": "A", "SOLAmount": 1 } ]""")[0]
            .Label.ShouldBe("W10");

    [Fact]
    public void Converts_sol_to_lamports()
    {
        WalletPool.ToLamports(2.54m).ShouldBe(2_540_000_000L);
        WalletPool.ToLamports(0.000000001m).ShouldBe(1L);
    }

    [Fact]
    public void Rejects_an_amount_finer_than_one_lamport()
        => Should.Throw<FunderException>(() => WalletPool.ToLamports(0.0000000001m));

    [Fact]
    public void Formats_amounts_invariantly_even_under_a_comma_decimal_culture()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("pt-BR");
            WalletPool.FormatSol(2.54m).ShouldBe("2.54");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void Rejects_malformed_json()
        => Should.Throw<FunderException>(() => WalletPool.Parse("not json"));
}
