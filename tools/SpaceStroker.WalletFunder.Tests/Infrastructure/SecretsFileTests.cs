using Shouldly;
using Xunit;
using SpaceStroker.WalletFunder.Domain;
using SpaceStroker.WalletFunder.Infrastructure;

namespace SpaceStroker.WalletFunder.Tests.Infrastructure;

public class SecretsFileTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"secrets-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private void WriteFile(
        string apiKeySecret = "real-secret-value",
        string apiKeyId = "organizations/o/apiKeys/k")
        => File.WriteAllText(_path, $$"""
        {
          "coinbase": { "apiKeyId": "{{apiKeyId}}", "apiKeySecret": "{{apiKeySecret}}",
                        "keyAlgorithm": "ed25519", "apiHost": "api.coinbase.com", "apiVersion": "2024-01-01" },
          "solana": { "rpcUrl": "https://api.mainnet-beta.solana.com" },
          "funding": { "spreadMinutes": 45, "networkFeeHeadroomSol": 0.05, "minAmountSol": 0.001,
                       "sendPollTimeoutSeconds": 300, "chainConfirmTimeoutSeconds": 120 },
          "wallets": [ { "Label": "W01", "PublicKey": "AAA", "PrivateKey": "LEAK", "SOLAmount": 2.54 } ]
        }
        """);

    [Fact]
    public void Loads_every_section()
    {
        WriteFile();

        var secrets = SecretsFile.Load(_path);

        secrets.Coinbase.ApiHost.ShouldBe("api.coinbase.com");
        secrets.Funding.SpreadMinutes.ShouldBe(45);
        secrets.Funding.NetworkFeeHeadroomSol.ShouldBe(0.05m);
        secrets.Solana.RpcUrl.ShouldBe("https://api.mainnet-beta.solana.com");
        secrets.Wallets.Count.ShouldBe(1);
        secrets.Wallets[0].SOLAmount.ShouldBe(2.54m);
    }

    [Fact]
    public void Rejects_a_placeholder_secret()
    {
        WriteFile(apiKeySecret: "REPLACE_ME_BASE64_ED25519_PRIVATE_KEY");

        Should.Throw<FunderException>(() => SecretsFile.Load(_path))
            .Message.ShouldContain("placeholder");
    }

    [Fact]
    public void Rejects_a_placeholder_key_id()
    {
        WriteFile(apiKeyId: "organizations/REPLACE_ME_ORG_ID/apiKeys/REPLACE_ME_KEY_ID");

        Should.Throw<FunderException>(() => SecretsFile.Load(_path)).Message.ShouldContain("placeholder");
    }

    [Fact]
    public void Reports_a_missing_file_with_the_fix()
        => Should.Throw<FunderException>(() => SecretsFile.Load(Path.Combine(Path.GetTempPath(), "nope.json")))
            .Message.ShouldContain("secrets.example.json");

    [Fact]
    public void Rejects_an_unknown_key_algorithm()
    {
        File.WriteAllText(_path, """
        { "coinbase": { "apiKeyId": "o/k", "apiKeySecret": "s", "keyAlgorithm": "rsa" }, "wallets": [] }
        """);

        Should.Throw<FunderException>(() => SecretsFile.Load(_path)).Message.ShouldContain("keyAlgorithm");
    }

    private void WriteWithTravelRule(string travelRule)
        => File.WriteAllText(_path, $$"""
        {
          "coinbase": { "apiKeyId": "o/k", "apiKeySecret": "s", "keyAlgorithm": "ed25519" },
          "travelRule": {{travelRule}},
          "wallets": []
        }
        """);

    [Fact]
    public void Loads_the_travel_rule_section()
    {
        WriteWithTravelRule("""
        { "beneficiaryWalletType": "WALLET_TYPE_SELF_HOSTED", "isSelf": "IS_SELF_TRUE",
          "beneficiaryName": "Satoshi Nakamoto", "beneficiaryCountry": "BR" }
        """);

        var travelRule = SecretsFile.Load(_path).TravelRule;

        travelRule.BeneficiaryWalletType.ShouldBe("WALLET_TYPE_SELF_HOSTED");
        travelRule.IsSelf.ShouldBe("IS_SELF_TRUE");
        travelRule.BeneficiaryName.ShouldBe("Satoshi Nakamoto");
        travelRule.BeneficiaryCountry.ShouldBe("BR");
    }

    [Fact]
    public void Defaults_the_travel_rule_section_to_unconfigured_when_absent()
    {
        WriteFile();

        // Absent ⇒ no travel_rule_data is sent, which is exactly the pre-travel-rule behavior.
        SecretsFile.Load(_path).TravelRule.BeneficiaryWalletType.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("WALLET_TYPE_SELF_HOSTED")]
    [InlineData("WALLET_TYPE_EXCHANGE")]
    public void Accepts_both_wallet_types(string walletType)
    {
        WriteWithTravelRule($$"""{ "beneficiaryWalletType": "{{walletType}}" }""");

        SecretsFile.Load(_path).TravelRule.BeneficiaryWalletType.ShouldBe(walletType);
    }

    [Fact]
    public void Rejects_an_unknown_beneficiary_wallet_type()
    {
        // Caught at load, not at send — a typo must never cost a live 400 mid-batch.
        WriteWithTravelRule("""{ "beneficiaryWalletType": "SELF_HOSTED" }""");

        Should.Throw<FunderException>(() => SecretsFile.Load(_path))
            .Message.ShouldContain("beneficiaryWalletType");
    }

    [Fact]
    public void Rejects_an_unknown_is_self_value()
    {
        WriteWithTravelRule("""
        { "beneficiaryWalletType": "WALLET_TYPE_SELF_HOSTED", "isSelf": "true" }
        """);

        Should.Throw<FunderException>(() => SecretsFile.Load(_path)).Message.ShouldContain("isSelf");
    }

    [Fact]
    public void Rejects_a_placeholder_beneficiary_name()
    {
        WriteWithTravelRule("""
        { "beneficiaryWalletType": "WALLET_TYPE_SELF_HOSTED", "beneficiaryName": "REPLACE_ME_ACCOUNT_HOLDER_NAME" }
        """);

        Should.Throw<FunderException>(() => SecretsFile.Load(_path)).Message.ShouldContain("placeholder");
    }

    [Fact]
    public void Rejects_a_placeholder_beneficiary_country()
    {
        // secrets.example.json ships REPLACE_ME here; an unedited copy must not reach Coinbase.
        WriteWithTravelRule("""
        { "beneficiaryWalletType": "WALLET_TYPE_SELF_HOSTED",
          "beneficiaryCountry": "REPLACE_ME_ISO_3166_ALPHA_2" }
        """);

        Should.Throw<FunderException>(() => SecretsFile.Load(_path))
            .Message.ShouldContain("beneficiaryCountry");
    }
}
