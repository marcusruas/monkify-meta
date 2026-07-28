using System.Text.Json;
using System.Text.Json.Serialization;
using Solnet.Wallet;
using Solnet.Wallet.Utilities;

namespace SpaceStroker.Seeder;

/// <summary>
/// The unversioned local deploy/secrets.seeder.json. Mirrors SpaceStroker.Deploy/SecretsFile.cs:
/// same JSON options, same fail-fast style, same "copy the example" guidance.
///
/// NOTHING here ever logs or re-emits WalletPrivateKeyBase58 — not even inside an exception
/// message. See BuildAccount for why that needs care.
/// </summary>
public sealed class SeederConfig
{
    [JsonPropertyName("apiBaseUrl")] public string ApiBaseUrl { get; set; } = "";
    [JsonPropertyName("walletPrivateKeyBase58")] public string WalletPrivateKeyBase58 { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("intervalHours")] public int IntervalHours { get; set; } = 6;
    [JsonPropertyName("retryAttempts")] public int RetryAttempts { get; set; } = 3;
    [JsonPropertyName("retryBaseDelaySeconds")] public int RetryBaseDelaySeconds { get; set; } = 5;
    [JsonPropertyName("confirmTimeoutSeconds")] public int ConfirmTimeoutSeconds { get; set; } = 90;

    /// <summary>Base58 public key derived from the secret key. Safe to log; the secret never is.</summary>
    [JsonIgnore] public string WalletPublicKey { get; private set; } = "";

    /// <summary>The signing account. Built once at load; never logged.</summary>
    [JsonIgnore] public Account Account { get; private set; } = null!;

    public static SeederConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new SeederException(
                $"Secrets file not found: {path}. Copy deploy/secrets.seeder.example.json and fill it in.");

        SeederConfig? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SeederConfig>(File.ReadAllText(path), JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new SeederException($"{path}: could not be parsed as JSON — {ex.Message}");
        }

        if (parsed is null)
            throw new SeederException($"{path}: could not be parsed.");

        parsed.Validate(path);
        return parsed;
    }

    private void Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(ApiBaseUrl)
            || !Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new SeederException($"{path}: 'apiBaseUrl' must be an absolute http(s) URL.");
        }

        ApiBaseUrl = ApiBaseUrl.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(WalletPrivateKeyBase58) || WalletPrivateKeyBase58 == "REPLACE_ME")
            throw new SeederException($"{path}: 'walletPrivateKeyBase58' is still a placeholder — fill in the real key.");

        if (string.IsNullOrWhiteSpace(DisplayName) || DisplayName == "REPLACE_ME")
            throw new SeederException($"{path}: 'displayName' is still a placeholder — fill it in.");

        if (IntervalHours <= 0)
            throw new SeederException($"{path}: 'intervalHours' must be greater than 0.");

        if (RetryAttempts < 0)
            throw new SeederException($"{path}: 'retryAttempts' cannot be negative.");

        if (RetryBaseDelaySeconds <= 0)
            throw new SeederException($"{path}: 'retryBaseDelaySeconds' must be greater than 0.");

        if (ConfirmTimeoutSeconds <= 0)
            throw new SeederException($"{path}: 'confirmTimeoutSeconds' must be greater than 0.");

        BuildAccount(path);
    }

    /// <summary>
    /// Solnet 6.1.0 has no Account.FromSecretKey. A Solana secret key is 64 bytes — the 32-byte
    /// private scalar followed by the 32-byte public key — and Solnet's Account(string, string)
    /// ctor takes both as base58. So decode, split, re-encode. (Round-trip verified against a
    /// generated keypair before this was written.)
    ///
    /// The catch deliberately swallows the inner exception's detail: a base58 decoder can echo the
    /// offending input — i.e. the key itself — back through its own message.
    /// </summary>
    private void BuildAccount(string path)
    {
        try
        {
            var secret = new Base58Encoder().DecodeData(WalletPrivateKeyBase58);

            if (secret.Length != 64)
                throw new SeederException(
                    $"{path}: 'walletPrivateKeyBase58' must decode to 64 bytes (got {secret.Length}).");

            WalletPublicKey = new Base58Encoder().EncodeData(secret[32..64]);
            Account = new Account(WalletPrivateKeyBase58, WalletPublicKey);
        }
        catch (SeederException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new SeederException($"{path}: 'walletPrivateKeyBase58' is not a valid base58 Solana secret key.");
        }
    }

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
