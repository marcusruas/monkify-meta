using Shouldly;
using Solnet.Wallet;
using Solnet.Wallet.Bip39;
using SpaceStroker.Nightfall.Domain;
using Xunit;

namespace SpaceStroker.Nightfall.Tests.Domain;

public class WalletPoolTests
{
    // A real, valid mainnet address (the SPL Token program) — used purely as a well-formed 32-byte key.
    private const string ValidAddress = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";

    [Fact]
    public void Ignores_unknown_fields_so_card_63s_pool_pastes_in_verbatim()
    {
        // #63's ghost-wallet pool carries SupplyPercentage and whatever else it grows. Binding must
        // not be strict, or the operator has to hand-edit the pool file before every run.
        var json = $$"""
        [
          { "Alias": "W01", "PublicKey": "{{ValidAddress}}", "PrivateKey": "k1", "SupplyPercentage": 0.4504, "Note": "x" }
        ]
        """;

        var pool = WalletPool.Parse(json);

        pool.Count.ShouldBe(1);
        pool[0].Alias.ShouldBe("W01");
        pool[0].PublicKey.ShouldBe(ValidAddress);
        pool[0].PrivateKey.ShouldBe("k1");
    }

    [Fact]
    public void Defaults_a_blank_alias_to_a_positional_label()
    {
        var pool = WalletPool.FromEntries(
        [
            new RawPoolEntry { PublicKey = ValidAddress, PrivateKey = "k1" },
            new RawPoolEntry { Alias = "   ", PublicKey = ValidAddress, PrivateKey = "k2" },
        ]);

        pool[0].Alias.ShouldBe("W01");
        pool[1].Alias.ShouldBe("W02");
    }

    [Fact]
    public void Trims_whitespace_around_an_address()
    {
        var pool = WalletPool.FromEntries([new RawPoolEntry { PublicKey = $"  {ValidAddress}  ", PrivateKey = "k" }]);

        pool[0].PublicKey.ShouldBe(ValidAddress);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base58-!!!")]
    [InlineData("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5D")]   // 31 bytes — a truncated address
    public void Rejects_an_address_that_is_not_a_32_byte_key(string address)
    {
        // Solnet's `new PublicKey(string)` is NOT validation — a truncated address is the
        // irreversible failure, so the decode is asserted explicitly.
        Should.Throw<NightfallException>(() => WalletPool.ValidateAddress(address, "W01"));
    }

    [Fact]
    public void Accepts_a_well_formed_address()
    {
        Should.NotThrow(() => WalletPool.ValidateAddress(ValidAddress, "W01"));
    }

    [Fact]
    public void Accepts_a_genuine_keypair()
    {
        // Proves the config-load check agrees with what Solnet actually produces — otherwise
        // ValidateKeyPair would reject every real wallet and nothing could ever load.
        var wallet = new Wallet(new Mnemonic(WordList.English, WordCount.Twelve));
        var entry = new PoolEntry("Real", wallet.Account.PublicKey.Key, wallet.Account.PrivateKey.Key);

        Should.NotThrow(() => WalletPool.ValidateKeyPair(entry));
    }

    [Fact]
    public void Rejects_a_key_that_belongs_to_a_different_address()
    {
        // The failure this catches is otherwise an opaque on-chain signature error, mid-batch,
        // after a quote has already been paid for.
        var mine = new Wallet(new Mnemonic(WordList.English, WordCount.Twelve));
        var theirs = new Wallet(new Mnemonic(WordList.English, WordCount.Twelve));
        var mismatched = new PoolEntry("Mismatched", theirs.Account.PublicKey.Key, mine.Account.PrivateKey.Key);

        Should.Throw<NightfallException>(() => WalletPool.ValidateKeyPair(mismatched))
            .Message.ShouldContain("different address");
    }
}

public class TokenAmountTests
{
    [Fact]
    public void Converts_raw_base_units_to_a_ui_amount_exactly()
    {
        TokenAmount.ToUi(12_345_678_900UL, 6).ShouldBe(12_345.6789m);
    }

    [Fact]
    public void Handles_zero_decimals()
    {
        TokenAmount.ToUi(42UL, 0).ShouldBe(42m);
    }

    [Fact]
    public void Does_not_lose_precision_on_a_large_supply()
    {
        // 1 billion tokens at 9 decimals. Through a double this comes back inexact.
        TokenAmount.ToUi(1_000_000_000_000_000_000UL, 9).ShouldBe(1_000_000_000m);
    }

    [Fact]
    public void Formats_invariantly_regardless_of_machine_culture()
    {
        // A pt-BR machine would otherwise render "2,54" and corrupt every number we display.
        TokenAmount.Format(2.54m).ShouldBe("2.54");
        TokenAmount.Format(1234.5m).ShouldBe("1234.5");
    }

    [Fact]
    public void Converts_lamports_to_sol()
    {
        TokenAmount.LamportsToSol(1_500_000_000L).ShouldBe(1.5m);
        TokenAmount.LamportsToSol(0L).ShouldBe(0m);
    }

    [Fact]
    public void Converts_sol_to_lamports()
    {
        TokenAmount.SolToLamports(0.003m).ShouldBe(3_000_000L);
    }
}
