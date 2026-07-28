using Shouldly;
using Solnet.Wallet.Utilities;
using Xunit;

namespace SpaceStroker.Seeder.Tests;

public class SeederConfigTests : IDisposable
{
    private readonly List<string> _temp = new();

    /// <summary>
    /// A synthetic 64-byte "secret key" built from a fixed byte pattern. Deliberately generated in
    /// code rather than pasted as a literal, so no string in this repo can be mistaken for a real
    /// Solana key. It controls no wallet and holds nothing.
    /// </summary>
    private static readonly string ValidKey =
        new Base58Encoder().EncodeData(Enumerable.Range(1, 64).Select(i => (byte)i).ToArray());

    private string WriteTemp(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"seeder-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        _temp.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _temp)
            if (File.Exists(path)) File.Delete(path);
    }

    private static string ValidJson() => $$"""
    {
      "apiBaseUrl": "https://api.spacestroker.com",
      "walletPrivateKeyBase58": "{{ValidKey}}",
      "displayName": "Edson",
      "intervalHours": 6,
      "retryAttempts": 3,
      "retryBaseDelaySeconds": 5,
      "confirmTimeoutSeconds": 90
    }
    """;

    [Fact]
    public void Load_parses_every_field()
    {
        var config = SeederConfig.Load(WriteTemp(ValidJson()));

        config.ApiBaseUrl.ShouldBe("https://api.spacestroker.com");
        config.DisplayName.ShouldBe("Edson");
        config.IntervalHours.ShouldBe(6);
        config.RetryAttempts.ShouldBe(3);
        config.RetryBaseDelaySeconds.ShouldBe(5);
        config.ConfirmTimeoutSeconds.ShouldBe(90);
        config.WalletPrivateKeyBase58.ShouldBe(ValidKey);
    }

    [Fact]
    public void Load_trims_a_trailing_slash_from_the_api_url()
    {
        var config = SeederConfig.Load(WriteTemp(
            ValidJson().Replace("https://api.spacestroker.com", "https://api.spacestroker.com/")));

        config.ApiBaseUrl.ShouldBe("https://api.spacestroker.com");
    }

    [Fact]
    public void Load_tolerates_comments_and_trailing_commas()
    {
        var json = $$"""
        {
          // the API
          "apiBaseUrl": "https://api.spacestroker.com",
          "walletPrivateKeyBase58": "{{ValidKey}}",
          "displayName": "Edson",
          "intervalHours": 6,
          "retryAttempts": 3,
          "retryBaseDelaySeconds": 5,
          "confirmTimeoutSeconds": 90,
        }
        """;

        Should.NotThrow(() => SeederConfig.Load(WriteTemp(json)));
    }

    [Fact]
    public void Load_throws_when_the_file_is_missing()
    {
        var ex = Should.Throw<SeederException>(
            () => SeederConfig.Load(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json")));

        ex.Message.ShouldContain("secrets.seeder.example.json");
    }

    [Theory]
    [InlineData("REPLACE_ME")]
    [InlineData("")]
    [InlineData("not-valid-base58-0OIl")]
    public void Load_throws_on_a_bad_wallet_key(string key)
    {
        var json = ValidJson().Replace(ValidKey, key);

        Should.Throw<SeederException>(() => SeederConfig.Load(WriteTemp(json)));
    }

    [Fact]
    public void Load_throws_when_the_key_is_valid_base58_but_not_64_bytes()
    {
        var shortKey = new Base58Encoder().EncodeData(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

        var ex = Should.Throw<SeederException>(
            () => SeederConfig.Load(WriteTemp(ValidJson().Replace(ValidKey, shortKey))));

        ex.Message.ShouldContain("64 bytes");
    }

    [Fact]
    public void Load_never_leaks_the_key_in_an_exception_message()
    {
        var json = ValidJson().Replace("\"intervalHours\": 6", "\"intervalHours\": 0");

        var ex = Should.Throw<SeederException>(() => SeederConfig.Load(WriteTemp(json)));

        ex.Message.ShouldNotContain(ValidKey);
    }

    [Fact]
    public void Load_never_leaks_a_malformed_key_in_an_exception_message()
    {
        // A base58 decoder can echo the offending input back through its own message.
        const string malformed = "totally-not-base58-!!!";

        var ex = Should.Throw<SeederException>(
            () => SeederConfig.Load(WriteTemp(ValidJson().Replace(ValidKey, malformed))));

        ex.Message.ShouldNotContain(malformed);
    }

    [Theory]
    [InlineData("\"displayName\": \"Edson\"", "\"displayName\": \"REPLACE_ME\"")]
    [InlineData("\"displayName\": \"Edson\"", "\"displayName\": \"\"")]
    [InlineData("\"intervalHours\": 6", "\"intervalHours\": 0")]
    [InlineData("\"intervalHours\": 6", "\"intervalHours\": -1")]
    [InlineData("\"retryAttempts\": 3", "\"retryAttempts\": -1")]
    [InlineData("\"retryBaseDelaySeconds\": 5", "\"retryBaseDelaySeconds\": 0")]
    [InlineData("\"confirmTimeoutSeconds\": 90", "\"confirmTimeoutSeconds\": 0")]
    [InlineData("\"apiBaseUrl\": \"https://api.spacestroker.com\"", "\"apiBaseUrl\": \"not-a-url\"")]
    [InlineData("\"apiBaseUrl\": \"https://api.spacestroker.com\"", "\"apiBaseUrl\": \"\"")]
    public void Load_throws_on_an_invalid_field(string from, string to)
    {
        Should.Throw<SeederException>(() => SeederConfig.Load(WriteTemp(ValidJson().Replace(from, to))));
    }

    [Fact]
    public void Zero_retry_attempts_is_allowed()
    {
        // 0 means "never retry", which is a legitimate choice — only a NEGATIVE count is nonsense.
        var config = SeederConfig.Load(WriteTemp(ValidJson().Replace("\"retryAttempts\": 3", "\"retryAttempts\": 0")));

        config.RetryAttempts.ShouldBe(0);
    }

    [Fact]
    public void The_public_key_is_derived_from_the_secret_key()
    {
        var config = SeederConfig.Load(WriteTemp(ValidJson()));

        // bytes[32..64] of the secret, base58-encoded.
        var expected = new Base58Encoder().EncodeData(
            Enumerable.Range(1, 64).Select(i => (byte)i).ToArray()[32..64]);

        config.WalletPublicKey.ShouldBe(expected);
        config.Account.PublicKey.Key.ShouldBe(expected);
    }
}
