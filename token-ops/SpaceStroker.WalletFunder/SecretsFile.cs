using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceStroker.WalletFunder;

/// <summary>
/// The unversioned local token-ops/secrets.json — the single source of truth for a funding run.
/// Mirrors deploy/tools/SpaceStroker.Deploy/SecretsFile.cs, including its fail-fast placeholder check.
/// </summary>
public sealed class SecretsFile
{
    [JsonPropertyName("coinbase")] public CoinbaseSection Coinbase { get; set; } = new();
    [JsonPropertyName("solana")] public SolanaSection Solana { get; set; } = new();
    [JsonPropertyName("funding")] public FundingSection Funding { get; set; } = new();
    [JsonPropertyName("wallets")] public List<RawPoolEntry> Wallets { get; set; } = [];

    public static SecretsFile Load(string path)
    {
        if (!File.Exists(path))
            throw new FunderException(
                $"Secrets file not found: {path}. Copy token-ops/secrets.example.json and fill it in.");

        SecretsFile parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SecretsFile>(File.ReadAllText(path), WalletPool.JsonOpts)
                     ?? throw new FunderException($"Could not parse {path}.");
        }
        catch (JsonException ex)
        {
            throw new FunderException($"Could not parse {path}: {ex.Message}");
        }

        if (IsPlaceholder(parsed.Coinbase.ApiKeyId))
            throw new FunderException($"{path}: coinbase.apiKeyId is still a placeholder — fill in the real key id.");

        if (IsPlaceholder(parsed.Coinbase.ApiKeySecret))
            throw new FunderException($"{path}: coinbase.apiKeySecret is still a placeholder — fill in the real key secret.");

        var algorithm = (parsed.Coinbase.KeyAlgorithm ?? "").Trim().ToLowerInvariant();
        if (algorithm is not ("ed25519" or "ecdsa"))
            throw new FunderException(
                $"{path}: coinbase.keyAlgorithm must be 'ed25519' or 'ecdsa' (was '{parsed.Coinbase.KeyAlgorithm}').");

        parsed.Coinbase.KeyAlgorithm = algorithm;

        return parsed;
    }

    private static bool IsPlaceholder(string? value)
        => string.IsNullOrWhiteSpace(value) || value.Contains("REPLACE_ME", StringComparison.OrdinalIgnoreCase);
}

public sealed class CoinbaseSection
{
    [JsonPropertyName("apiKeyId")] public string ApiKeyId { get; set; } = "";

    /// <summary>Never printed, never logged, never written to receipts.</summary>
    [JsonPropertyName("apiKeySecret")] public string ApiKeySecret { get; set; } = "";

    [JsonPropertyName("keyAlgorithm")] public string KeyAlgorithm { get; set; } = "ed25519";
    [JsonPropertyName("accountId")] public string AccountId { get; set; } = "";
    [JsonPropertyName("apiHost")] public string ApiHost { get; set; } = "api.coinbase.com";
    [JsonPropertyName("apiVersion")] public string ApiVersion { get; set; } = "2024-01-01";
}

public sealed class SolanaSection
{
    [JsonPropertyName("rpcUrl")] public string RpcUrl { get; set; } = "https://api.mainnet-beta.solana.com";
}

public sealed class FundingSection
{
    [JsonPropertyName("spreadMinutes")] public int SpreadMinutes { get; set; } = 45;
    [JsonPropertyName("networkFeeHeadroomSol")] public decimal NetworkFeeHeadroomSol { get; set; } = 0.05m;
    [JsonPropertyName("minAmountSol")] public decimal MinAmountSol { get; set; } = 0.001m;
    [JsonPropertyName("sendPollTimeoutSeconds")] public int SendPollTimeoutSeconds { get; set; } = 300;
    [JsonPropertyName("chainConfirmTimeoutSeconds")] public int ChainConfirmTimeoutSeconds { get; set; } = 120;
}
