using System.Text.Json;
using System.Text.Json.Serialization;
using SpaceStroker.Nightfall.Domain;

namespace SpaceStroker.Nightfall.Infrastructure;

/// <summary>
/// The unversioned local tools/secrets.nightfall.json — the single source of truth for a session.
/// Mirrors SpaceStroker.WalletFunder/Infrastructure/SecretsFile.cs, including its fail-fast
/// placeholder check.
///
/// ⚠️ Deliberately its OWN file rather than sharing tools/secrets.json: that one holds live Coinbase
/// withdrawal credentials, and mixing them with spend keys puts two very different blast radii in
/// one place.
/// </summary>
public sealed class SecretsFile
{
    [JsonPropertyName("solana")] public SolanaSection Solana { get; set; } = new();
    [JsonPropertyName("token")] public TokenSection Token { get; set; } = new();
    [JsonPropertyName("jupiter")] public JupiterSection Jupiter { get; set; } = new();
    [JsonPropertyName("sell")] public SellSection Sell { get; set; } = new();
    [JsonPropertyName("wallets")] public List<RawPoolEntry> Wallets { get; set; } = [];

    [JsonIgnore] public IReadOnlyList<PoolEntry> Pool { get; private set; } = [];

    public static SecretsFile Load(string path)
    {
        if (!File.Exists(path))
            throw new NightfallException(
                $"Secrets file not found: {path}. Copy tools/secrets.nightfall.example.json and fill it in.");

        SecretsFile parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SecretsFile>(File.ReadAllText(path), WalletPool.JsonOpts)
                     ?? throw new NightfallException($"Could not parse {path}.");
        }
        catch (JsonException ex)
        {
            throw new NightfallException($"Could not parse {path}: {ex.Message}");
        }

        if (IsPlaceholder(parsed.Solana.RpcUrl))
            throw new NightfallException($"{path}: solana.rpcUrl is still a placeholder — fill in the real RPC endpoint.");

        if (IsPlaceholder(parsed.Token.Mint))
            throw new NightfallException($"{path}: token.mint is still a placeholder — fill in the Token CA.");

        WalletPool.ValidateAddress(parsed.Token.Mint.Trim(), "token.mint");
        parsed.Token.Mint = parsed.Token.Mint.Trim();

        if (parsed.Jupiter.SlippageBps is < 0 or > 10_000)
            throw new NightfallException($"{path}: jupiter.slippageBps must be between 0 and 10000.");

        if (parsed.Jupiter.MaxPriceImpactPercent is < 0m or > 100m)
            throw new NightfallException($"{path}: jupiter.maxPriceImpactPercent must be between 0 and 100.");

        if (parsed.Sell.MaxConcurrentSells < 1)
            throw new NightfallException($"{path}: sell.maxConcurrentSells must be at least 1.");

        // An EMPTY pool is not an error here — Program reports it and exits 0, matching the funder.
        parsed.Pool = WalletPool.FromEntries(parsed.Wallets);

        foreach (var entry in parsed.Pool)
        {
            if (IsPlaceholder(entry.PublicKey) || IsPlaceholder(entry.PrivateKey))
                throw new NightfallException(
                    $"{path}: wallet '{entry.Alias}' still has a placeholder key — fill it in or remove the entry.");

            WalletPool.ValidateAddress(entry.PublicKey, entry.Alias);

            // Proving the key controls the address at LOAD time turns an opaque mid-batch signing
            // failure (after a quote, with money in motion) into a startup error.
            WalletPool.ValidateKeyPair(entry);
        }

        var duplicate = parsed.Pool.GroupBy(e => e.PublicKey).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new NightfallException($"{path}: the address {duplicate.Key} appears more than once in the pool.");

        return parsed;
    }

    private static bool IsPlaceholder(string? value)
        => string.IsNullOrWhiteSpace(value) || value.Contains("REPLACE_ME", StringComparison.OrdinalIgnoreCase);
}

public sealed class SolanaSection
{
    /// <summary>
    /// The keyed endpoint. Used for EVERY read and every broadcast.
    /// ⚠️ Carries the API key in its query string — mask it to host-only wherever it is logged.
    /// </summary>
    [JsonPropertyName("rpcUrl")] public string RpcUrl { get; set; } = "";
}

public sealed class TokenSection
{
    /// <summary>The Token CA. Config-only — never taken from the browser.</summary>
    [JsonPropertyName("mint")] public string Mint { get; set; } = "";
}

public sealed class JupiterSection
{
    /// <summary>
    /// Base for /quote and /swap. Jupiter moved off quote-api.jup.ag/v6 to lite-api.jup.ag/swap/v1
    /// (free) and api.jup.ag/swap/v1 (keyed); this is configurable so a move needs no code change.
    /// </summary>
    [JsonPropertyName("apiBaseUrl")] public string ApiBaseUrl { get; set; } = "https://lite-api.jup.ag/swap/v1";

    [JsonPropertyName("slippageBps")] public int SlippageBps { get; set; } = 300;
    [JsonPropertyName("maxPriceImpactPercent")] public decimal MaxPriceImpactPercent { get; set; } = 10m;
    [JsonPropertyName("prioritizationFeeLamports")] public long PrioritizationFeeLamports { get; set; }
    [JsonPropertyName("quoteCacheSeconds")] public int QuoteCacheSeconds { get; set; } = 10;
}

public sealed class SellSection
{
    [JsonPropertyName("maxConcurrentSells")] public int MaxConcurrentSells { get; set; } = 4;
    [JsonPropertyName("minSolForFeeSol")] public decimal MinSolForFeeSol { get; set; } = 0.003m;
    [JsonPropertyName("refreshIntervalSeconds")] public int RefreshIntervalSeconds { get; set; } = 15;
    [JsonPropertyName("confirmTimeoutSeconds")] public int ConfirmTimeoutSeconds { get; set; } = 90;
    [JsonPropertyName("closeEmptiedTokenAccounts")] public bool CloseEmptiedTokenAccounts { get; set; } = true;
}
