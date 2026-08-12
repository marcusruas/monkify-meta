using System.Text.Json;
using System.Text.Json.Serialization;
using SpaceStroker.WalletFunder.Domain;

namespace SpaceStroker.WalletFunder.Infrastructure;

/// <summary>
/// The unversioned local tools/secrets.json — the single source of truth for a funding run.
/// Mirrors deploy/tools/SpaceStroker.Deploy/SecretsFile.cs, including its fail-fast placeholder check.
/// </summary>
public sealed class SecretsFile
{
    [JsonPropertyName("coinbase")] public CoinbaseSection Coinbase { get; set; } = new();
    [JsonPropertyName("travelRule")] public TravelRuleSection TravelRule { get; set; } = new();
    [JsonPropertyName("solana")] public SolanaSection Solana { get; set; } = new();
    [JsonPropertyName("funding")] public FundingSection Funding { get; set; } = new();
    [JsonPropertyName("wallets")] public List<RawPoolEntry> Wallets { get; set; } = [];

    public static SecretsFile Load(string path)
    {
        if (!File.Exists(path))
            throw new FunderException(
                $"Secrets file not found: {path}. Copy tools/secrets.example.json and fill it in.");

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

        ValidateTravelRule(parsed.TravelRule, path);

        return parsed;
    }

    /// <summary>
    /// Travel-rule values are enums on Coinbase's side. Validating them here turns a typo into a
    /// load-time error instead of an HTTP 400 in the middle of a live batch.
    /// </summary>
    private static void ValidateTravelRule(TravelRuleSection travelRule, string path)
    {
        var walletType = (travelRule.BeneficiaryWalletType ?? "").Trim();

        // Blank is legitimate: it means "this account does not need travel-rule data".
        if (walletType.Length == 0)
        {
            travelRule.BeneficiaryWalletType = "";
            return;
        }

        if (walletType is not ("WALLET_TYPE_SELF_HOSTED" or "WALLET_TYPE_EXCHANGE"))
            throw new FunderException(
                $"{path}: travelRule.beneficiaryWalletType must be 'WALLET_TYPE_SELF_HOSTED' or " +
                $"'WALLET_TYPE_EXCHANGE' (was '{travelRule.BeneficiaryWalletType}').");

        var isSelf = (travelRule.IsSelf ?? "").Trim();
        if (isSelf.Length > 0 && isSelf is not ("IS_SELF_TRUE" or "IS_SELF_FALSE"))
            throw new FunderException(
                $"{path}: travelRule.isSelf must be 'IS_SELF_TRUE' or 'IS_SELF_FALSE' (was '{travelRule.IsSelf}').");

        RejectPlaceholder(travelRule.BeneficiaryName, "beneficiaryName", "the account holder name", path);
        RejectPlaceholder(travelRule.BeneficiaryCountry, "beneficiaryCountry", "an ISO 3166-1 alpha-2 code", path);

        travelRule.BeneficiaryWalletType = walletType;
        travelRule.IsSelf = isSelf;
    }

    /// <summary>Blank is fine (the field may not apply); a leftover REPLACE_ME is not.</summary>
    private static void RejectPlaceholder(string? value, string field, string fix, string path)
    {
        if (!string.IsNullOrWhiteSpace(value) && IsPlaceholder(value))
            throw new FunderException($"{path}: travelRule.{field} is still a placeholder — fill in {fix}.");
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

/// <summary>
/// Travel Rule data Coinbase attaches to an outbound send. Which fields are required depends on the
/// jurisdiction of the sending account — US/Canada self-hosted sends need only the wallet type, while
/// GB/EU/SG/HK/PH also need is_self, the beneficiary name, and the country. Leaving
/// <see cref="BeneficiaryWalletType"/> blank sends no travel_rule_data at all.
/// </summary>
public sealed class TravelRuleSection
{
    [JsonPropertyName("beneficiaryWalletType")] public string BeneficiaryWalletType { get; set; } = "";
    [JsonPropertyName("isSelf")] public string IsSelf { get; set; } = "";
    [JsonPropertyName("beneficiaryName")] public string BeneficiaryName { get; set; } = "";

    /// <summary>ISO 3166-1 alpha-2. Coinbase nests it as beneficiary_address.country.</summary>
    [JsonPropertyName("beneficiaryCountry")] public string BeneficiaryCountry { get; set; } = "";

    [JsonPropertyName("beneficiaryAddress1")] public string BeneficiaryAddress1 { get; set; } = "";
    [JsonPropertyName("beneficiaryCity")] public string BeneficiaryCity { get; set; } = "";
    [JsonPropertyName("beneficiaryState")] public string BeneficiaryState { get; set; } = "";
    [JsonPropertyName("beneficiaryPostalCode")] public string BeneficiaryPostalCode { get; set; } = "";

    /// <summary>Only for WALLET_TYPE_EXCHANGE sends — the receiving institution's identifier.</summary>
    [JsonPropertyName("beneficiaryFinancialInstitution")] public string BeneficiaryFinancialInstitution { get; set; } = "";

    [JsonPropertyName("transferPurpose")] public string TransferPurpose { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BeneficiaryWalletType);
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
