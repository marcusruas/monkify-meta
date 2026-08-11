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
        var path = Path.Combine(_directory, "secrets.json");
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>
    /// A shared-shape secrets file carrying the other tools' sections alongside Nightfall's.
    /// The pool is EMPTY on purpose: WalletPool.ValidateKeyPair proves a wallet's key against its
    /// address at load time, and this file has no valid keypair constant (every pool test below
    /// either uses [] or asserts a deliberate rejection). An empty pool loads cleanly, which is
    /// exactly what these tests need — the section binding, not the pool.
    /// </summary>
    private static string SharedShape(string nightfallBlock) => $$"""
    {
      "solana": { "rpcUrl": "https://rpc.example/?api-key=test" },
      "coinbase": { "apiKeyId": "id", "apiKeySecret": "secret", "keyAlgorithm": "ed25519" },
      "funding": { "spreadMinutes": 45 },
      "snipe": { "mint": "{{TokenMint}}" },
      {{nightfallBlock}}
      "wallets": []
    }
    """;

    private static string NightfallBlock(string mint = TokenMint, int slippageBps = 300,
                                         decimal maxImpact = 10m, int concurrency = 4) => $$"""
      "nightfall": {
        "token": { "mint": "{{mint}}" },
        "jupiter": { "apiBaseUrl": "https://lite-api.jup.ag/swap/v1", "slippageBps": {{slippageBps}},
                     "maxPriceImpactPercent": {{maxImpact}}, "prioritizationFeeLamports": 0,
                     "quoteCacheSeconds": 10 },
        "sell": { "maxConcurrentSells": {{concurrency}}, "minSolForFeeSol": 0.003,
                  "refreshIntervalSeconds": 15, "confirmTimeoutSeconds": 90,
                  "closeEmptiedTokenAccounts": true }
      },
    """;

    [Fact]
    public void A_missing_file_names_both_the_file_and_the_template()
    {
        // AC01. "Not found" alone leaves the operator guessing which file to create.
        var missing = Path.Combine(_directory, "nope.json");

        var ex = Should.Throw<NightfallException>(() => SecretsFile.Load(missing));

        ex.Message.ShouldContain("nope.json");
        ex.Message.ShouldContain("secrets.example.json");
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
        // rpcUrl is validated BEFORE the nightfall section, so this fixture legitimately omits it.
        var path = Write($$"""
        { "solana": { "rpcUrl": "https://x/?api-key=REPLACE_ME_RPC_KEY" },
          "nightfall": { "token": { "mint": "{{TokenMint}}" } }, "wallets": [] }
        """);

        var ex = Should.Throw<NightfallException>(() => SecretsFile.Load(path));
        ex.Message.ShouldContain("solana.rpcUrl");
    }

    [Fact]
    public void The_shared_file_binds_nightfall_config_from_its_own_section()
    {
        var path = Write(SharedShape(NightfallBlock()));

        var secrets = SecretsFile.Load(path);

        secrets.Nightfall.Token.Mint.ShouldBe(TokenMint);
        secrets.Nightfall.Jupiter.SlippageBps.ShouldBe(300);
        secrets.Nightfall.Sell.MaxConcurrentSells.ShouldBe(4);
        secrets.Nightfall.Jupiter.ApiBaseUrl.ShouldBe("https://lite-api.jup.ag/swap/v1");
        secrets.Solana.RpcUrl.ShouldBe("https://rpc.example/?api-key=test");
    }

    [Fact]
    public void The_other_tools_sections_in_the_shared_file_are_ignored()
    {
        // The file is SHARED. Nightfall must load cleanly past the funder's coinbase/funding blocks
        // and the sniper's snipe block, exactly as those tools ignore the nightfall block.
        var path = Write(SharedShape(NightfallBlock()));

        Should.NotThrow(() => SecretsFile.Load(path));
    }

    [Fact]
    public void A_missing_nightfall_section_is_named_explicitly()
    {
        // Without this branch the operator gets "nightfall.token.mint is still a placeholder",
        // which sends them looking for the wrong problem: the section simply is not there yet.
        var path = Write(SharedShape(nightfallBlock: ""));

        var ex = Should.Throw<NightfallException>(() => SecretsFile.Load(path));

        ex.Message.ShouldContain("nightfall");
        ex.Message.ShouldContain("secrets.example.json");
    }

    [Fact]
    public void A_placeholder_token_mint_is_rejected_by_name()
    {
        var path = Write(SharedShape(NightfallBlock(mint: "REPLACE_ME_TOKEN_MINT_ADDRESS")));

        var ex = Should.Throw<NightfallException>(() => SecretsFile.Load(path));
        ex.Message.ShouldContain("nightfall.token.mint");
    }

    [Fact]
    public void Validation_messages_carry_the_nightfall_prefix()
    {
        // A message naming a key that no longer exists is worse than no message.
        var badSlippage = Write(SharedShape(NightfallBlock(slippageBps: 10_001)));
        Should.Throw<NightfallException>(() => SecretsFile.Load(badSlippage))
              .Message.ShouldContain("nightfall.jupiter.slippageBps");

        var badImpact = Write(SharedShape(NightfallBlock(maxImpact: 101m)));
        Should.Throw<NightfallException>(() => SecretsFile.Load(badImpact))
              .Message.ShouldContain("nightfall.jupiter.maxPriceImpactPercent");

        var badConcurrency = Write(SharedShape(NightfallBlock(concurrency: 0)));
        Should.Throw<NightfallException>(() => SecretsFile.Load(badConcurrency))
              .Message.ShouldContain("nightfall.sell.maxConcurrentSells");
    }

    [Fact]
    public void A_placeholder_wallet_key_is_rejected_by_alias()
    {
        var path = Write($$"""
        { "solana": { "rpcUrl": "https://rpc.example/?api-key=k" },
          "nightfall": { "token": { "mint": "{{TokenMint}}" } },
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
          "nightfall": { "token": { "mint": "{{TokenMint}}" } }, "wallets": [] }
        """);

        var secrets = SecretsFile.Load(path);

        secrets.Pool.ShouldBeEmpty();
    }

    [Fact]
    public void Applies_the_documented_defaults_when_a_section_is_absent()
    {
        // The nightfall section is present but carries only the mint: jupiter and sell still default.
        var path = Write($$"""
        { "solana": { "rpcUrl": "https://rpc.example/?api-key=k" },
          "nightfall": { "token": { "mint": "{{TokenMint}}" } }, "wallets": [] }
        """);

        var secrets = SecretsFile.Load(path);

        secrets.Nightfall.Jupiter.SlippageBps.ShouldBe(300);
        secrets.Nightfall.Jupiter.MaxPriceImpactPercent.ShouldBe(10m);
        secrets.Nightfall.Jupiter.QuoteCacheSeconds.ShouldBe(10);
        secrets.Nightfall.Sell.MaxConcurrentSells.ShouldBe(4);
        secrets.Nightfall.Sell.MinSolForFeeSol.ShouldBe(0.003m);
        secrets.Nightfall.Sell.RefreshIntervalSeconds.ShouldBe(15);
        secrets.Nightfall.Sell.CloseEmptiedTokenAccounts.ShouldBeTrue();
    }

    [Fact]
    public void Rejects_an_out_of_range_slippage()
    {
        var path = Write($$"""
        { "solana": { "rpcUrl": "https://rpc.example/?api-key=k" },
          "nightfall": { "token": { "mint": "{{TokenMint}}" }, "jupiter": { "slippageBps": 20000 } },
          "wallets": [] }
        """);

        Should.Throw<NightfallException>(() => SecretsFile.Load(path))
              .Message.ShouldContain("nightfall.jupiter.slippageBps");
    }

    [Fact]
    public void Rejects_a_wallet_whose_address_is_not_a_valid_key()
    {
        var path = Write($$"""
        { "solana": { "rpcUrl": "https://rpc.example/?api-key=k" },
          "nightfall": { "token": { "mint": "{{TokenMint}}" } },
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
          "nightfall": { "token": { "mint": "{{TokenMint}}" } },
          "wallets": [ { "Alias": "Mismatched", "PublicKey": "{{TestPublicKey}}", "PrivateKey": "3TmQrJ2vZKzXhLmNpQrStUvWxYz1234567890AbCdEfGhIjKlMnOpQrStUvWxYz12" } ] }
        """);

        Should.Throw<NightfallException>(() => SecretsFile.Load(path)).Message.ShouldContain("Mismatched");
    }
}
