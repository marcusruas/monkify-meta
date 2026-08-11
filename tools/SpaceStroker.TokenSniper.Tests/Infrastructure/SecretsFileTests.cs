using Shouldly;
using SpaceStroker.TokenSniper.Domain;
using SpaceStroker.TokenSniper.Infrastructure;
using Xunit;

namespace SpaceStroker.TokenSniper.Tests.Infrastructure;

public class SecretsFileTests
{
    private const string ValidMint = "6RTQtuhu19q4LnCxbJLPEWjS1USoqAbSBLkM93tXpump";

    [Fact]
    public void Parse_ReadsTheSnipeSectionAndTheSharedPool()
    {
        var secrets = SecretsFile.Parse(Json(), "secrets.json");

        secrets.Solana.RpcUrl.ShouldBe("https://rpc.example/?api-key=abc");
        secrets.Snipe.Mint.ShouldBe(ValidMint);
        secrets.Snipe.SpreadMinutes.ShouldBe(60);
        secrets.Snipe.SlippageTolerance.ShouldBe(0.25m);
        secrets.Wallets.Count.ShouldBe(1);
        secrets.Wallets[0].Alias.ShouldBe("W01");
        secrets.Wallets[0].SupplyPercentage.ShouldBe(0.4504m);
    }

    [Fact]
    public void Parse_IgnoresTheFundersOwnSectionsInTheSharedFile()
    {
        var json = $$"""
            {
              "coinbase": { "apiKeyId": "id", "apiKeySecret": "secret" },
              "travelRule": { "beneficiaryWalletType": "WALLET_TYPE_SELF_HOSTED" },
              "funding": { "spreadMinutes": 45 },
              "solana": { "rpcUrl": "https://rpc.example/?api-key=abc" },
              "snipe": { "mint": "{{ValidMint}}" },
              "wallets": [ { "alias": "W01", "Label": "W01", "PublicKey": "addr", "PrivateKey": "k", "SOLAmount": 2.54, "SupplyPercentage": 1 } ]
            }
            """;

        Should.NotThrow(() => SecretsFile.Parse(json, "secrets.json"));
    }

    [Fact]
    public void Parse_RejectsAPlaceholderRpcUrl()
    {
        var json = Json(rpcUrl: "https://mainnet.helius-rpc.com/?api-key=REPLACE_ME_RPC_KEY");

        Should.Throw<SniperException>(() => SecretsFile.Parse(json, "secrets.json"))
            .Message.ShouldContain("placeholder");
    }

    [Fact]
    public void Parse_RejectsAPlaceholderMint()
    {
        var json = Json(mint: "REPLACE_ME_TOKEN_MINT_ADDRESS");

        Should.Throw<SniperException>(() => SecretsFile.Parse(json, "secrets.json"))
            .Message.ShouldContain("snipe.mint");
    }

    [Fact]
    public void Parse_RejectsAMintThatIsNotAnAddress()
    {
        var json = Json(mint: "not-an-address");

        Should.Throw<SniperException>(() => SecretsFile.Parse(json, "secrets.json"))
            .Message.ShouldContain("not a valid Solana address");
    }

    [Fact]
    public void Parse_RejectsNegativeSlippageOrSpread()
    {
        Should.Throw<SniperException>(() => SecretsFile.Parse(Json(slippage: "-0.1"), "secrets.json"))
            .Message.ShouldContain("slippageTolerance");

        Should.Throw<SniperException>(() => SecretsFile.Parse(Json(spread: "-5"), "secrets.json"))
            .Message.ShouldContain("spreadMinutes");
    }

    [Fact]
    public void Parse_RejectsAnExtraAccountThatIsNotAnAddress()
    {
        var json = Json(extraSnipe: "\"extraAccounts\": [\"nope\"],");

        Should.Throw<SniperException>(() => SecretsFile.Parse(json, "secrets.json"))
            .Message.ShouldContain("extraAccounts");
    }

    [Fact]
    public void Parse_DefaultsTrackVolumeToOmittedWhichIsTheCommonestLiveEncoding()
    {
        SecretsFile.Parse(Json(), "secrets.json").Snipe.TrackVolume.ShouldBeNull();
    }

    [Fact]
    public void Load_SaysWhereToGetATemplateWhenTheFileIsMissing()
    {
        Should.Throw<SniperException>(() => SecretsFile.Load(Path.Combine(Path.GetTempPath(), "no-such-secrets.json")))
            .Message.ShouldContain("secrets.example.json");
    }

    [Fact]
    public void Parse_TurnsMalformedJsonIntoAnActionableError()
    {
        Should.Throw<SniperException>(() => SecretsFile.Parse("{ nope", "secrets.json"))
            .Message.ShouldContain("Could not parse");
    }

    private static string Json(
        string rpcUrl = "https://rpc.example/?api-key=abc",
        string mint = ValidMint,
        string spread = "60",
        string slippage = "0.25",
        string extraSnipe = "") =>
        $$"""
        {
          "solana": { "rpcUrl": "{{rpcUrl}}" },
          "snipe": { {{extraSnipe}} "mint": "{{mint}}", "spreadMinutes": {{spread}}, "slippageTolerance": {{slippage}} },
          "wallets": [
            { "alias": "W01", "PublicKey": "addr", "PrivateKey": "k", "SupplyPercentage": 0.4504 }
          ]
        }
        """;
}
