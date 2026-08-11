using Shouldly;
using Xunit;

namespace SpaceStroker.WalletFunder.Tests;

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
}
