using Shouldly;
using SpaceStroker.Nightfall.Domain;
using SpaceStroker.Nightfall.Infrastructure;
using Xunit;

namespace SpaceStroker.Nightfall.Tests.Infrastructure;

public class SecretsFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "nightfall-tests", Guid.NewGuid().ToString("N"));

    // A real keypair generated for tests only — it holds nothing and controls nothing.
    private const string TestPublicKey = "6dVJ5PdWXQnBBUAQFvJfBpLhhCKKKFAnKxKKUKgRfKKW";
    private const string TokenMint = "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v";

    public SecretsFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private string Write(string json)
    {
        var path = Path.Combine(_directory, "secrets.nightfall.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void A_missing_file_names_both_the_file_and_the_template()
    {
        // AC01. "Not found" alone leaves the operator guessing which file to create.
        var missing = Path.Combine(_directory, "nope.json");

        var ex = Should.Throw<NightfallException>(() => SecretsFile.Load(missing));

        ex.Message.ShouldContain("nope.json");
        ex.Message.ShouldContain("secrets.nightfall.example.json");
    }

    [Fact]
    public void Unparsable_json_is_reported_with_the_path()
    {
        var path = Write("{ this is not json");

        var ex = Should.Throw<NightfallException>(() => SecretsFile.Load(path));
        ex.Message.ShouldContain(path);
    }

    [Fact]
    public void A_placeholder_rpc_url_is_rejected_by_name()
    {
        var path = Write($$"""
        { "solana": { "rpcUrl": "https://x/?api-key=REPLACE_ME_RPC_KEY" },
          "token": { "mint": "{{TokenMint}}" }, "wallets": [] }
        """);

        var ex = Should.Throw<NightfallException>(() => SecretsFile.Load(path));
        ex.Message.ShouldContain("solana.rpcUrl");
    }

    [Fact]
    public void A_placeholder_token_mint_is_rejected_by_name()
    {
        var path = Write("""
        { "solana": { "rpcUrl": "https://rpc.example/?api-key=k" },
          "token": { "mint": "REPLACE_ME_TOKEN_MINT_ADDRESS" }, "wallets": [] }
        """);

        var ex = Should.Throw<NightfallException>(() => SecretsFile.Load(path));
        ex.Message.ShouldContain("token.mint");
    }

    [Fact]
    public void A_placeholder_wallet_key_is_rejected_by_alias()
    {
        var path = Write($$"""
        { "solana": { "rpcUrl": "https://rpc.example/?api-key=k" },
          "token": { "mint": "{{TokenMint}}" },
          "wallets": [ { "Alias": "DEV Wallet", "PublicKey": "REPLACE_ME_BASE58_ADDRESS", "PrivateKey": "REPLACE_ME" } ] }
        """);

        var ex = Should.Throw<NightfallException>(() => SecretsFile.Load(path));
        ex.Message.ShouldContain("DEV Wallet");
    }

    [Fact]
    public void An_empty_pool_loads_cleanly_so_the_host_can_report_it_and_exit_zero()
    {
        // AC03. An empty pool is a no-op with a clear message, not a crash.
        var path = Write($$"""
        { "solana": { "rpcUrl": "https://rpc.example/?api-key=k" },
          "token": { "mint": "{{TokenMint}}" }, "wallets": [] }
        """);

        var secrets = SecretsFile.Load(path);

        secrets.Pool.ShouldBeEmpty();
    }

    [Fact]
    public void Applies_the_documented_defaults_when_a_section_is_absent()
    {
        var path = Write($$"""
        { "solana": { "rpcUrl": "https://rpc.example/?api-key=k" },
          "token": { "mint": "{{TokenMint}}" }, "wallets": [] }
        """);

        var secrets = SecretsFile.Load(path);

        secrets.Jupiter.SlippageBps.ShouldBe(300);
        secrets.Jupiter.MaxPriceImpactPercent.ShouldBe(10m);
        secrets.Jupiter.QuoteCacheSeconds.ShouldBe(10);
        secrets.Sell.MaxConcurrentSells.ShouldBe(4);
        secrets.Sell.MinSolForFeeSol.ShouldBe(0.003m);
        secrets.Sell.RefreshIntervalSeconds.ShouldBe(15);
        secrets.Sell.CloseEmptiedTokenAccounts.ShouldBeTrue();
    }

    [Fact]
    public void Rejects_an_out_of_range_slippage()
    {
        var path = Write($$"""
        { "solana": { "rpcUrl": "https://rpc.example/?api-key=k" },
          "token": { "mint": "{{TokenMint}}" }, "jupiter": { "slippageBps": 20000 }, "wallets": [] }
        """);

        Should.Throw<NightfallException>(() => SecretsFile.Load(path)).Message.ShouldContain("slippageBps");
    }

    [Fact]
    public void Rejects_a_wallet_whose_address_is_not_a_valid_key()
    {
        var path = Write($$"""
        { "solana": { "rpcUrl": "https://rpc.example/?api-key=k" },
          "token": { "mint": "{{TokenMint}}" },
          "wallets": [ { "Alias": "Bad", "PublicKey": "truncated", "PrivateKey": "whatever" } ] }
        """);

        Should.Throw<NightfallException>(() => SecretsFile.Load(path)).Message.ShouldContain("Bad");
    }

    [Fact]
    public void Rejects_a_private_key_that_does_not_control_its_stated_address()
    {
        // Otherwise this surfaces as an opaque on-chain signature failure mid-batch, after a quote.
        var path = Write($$"""
        { "solana": { "rpcUrl": "https://rpc.example/?api-key=k" },
          "token": { "mint": "{{TokenMint}}" },
          "wallets": [ { "Alias": "Mismatched", "PublicKey": "{{TestPublicKey}}", "PrivateKey": "3TmQrJ2vZKzXhLmNpQrStUvWxYz1234567890AbCdEfGhIjKlMnOpQrStUvWxYz12" } ] }
        """);

        Should.Throw<NightfallException>(() => SecretsFile.Load(path)).Message.ShouldContain("Mismatched");
    }
}
