using System.Text.Json;
using System.Text.Json.Serialization;
using SpaceStroker.Nightfall.Domain;

namespace SpaceStroker.Nightfall.Infrastructure;

/// <summary>
/// The unversioned local tools/secrets.json — SHARED with spacestroker-fund-wallets and
/// spacestroker-snipe-token. This tool reads the top-level `solana` section, its own `nightfall`
/// section (token / jupiter / sell) and the shared `wallets` pool; the other tools' sections are
/// ignored here, exactly as this tool's are ignored there. Mirrors
/// SpaceStroker.WalletFunder/Infrastructure/SecretsFile.cs, including its fail-fast placeholder check.
///
/// ⚠️ This file holds LIVE WALLET PRIVATE KEYS and live Coinbase withdrawal credentials. It is
/// git-ignored (tools/.gitignore) and nothing from it beyond public addresses may reach console
/// output, receipts or a report.
///
/// Note: card #166 originally kept these settings in their own secrets.nightfall.json, reasoning that
/// tools/secrets.json held withdrawal credentials — "a different blast radius". Card #167 merged them
/// because that had already stopped being true: card #63's sniper binds wallet PrivateKeys out of this
/// same file, so spend keys and withdrawal credentials were already co-located.
/// </summary>
public sealed class SecretsFile
{
    [JsonPropertyName("solana")] public SolanaSection Solana { get; set; } = new();

    /// <summary>
    /// Nullable with NO initializer on purpose: that is what makes "the section is absent" (an
    /// operator who has not hand-merged yet) distinguishable from "present but empty". Load assigns
    /// it before returning, so every call site can use it without a null check.
    /// </summary>
    [JsonPropertyName("nightfall")] public NightfallSection? Nightfall { get; set; }

    [JsonPropertyName("wallets")] public List<RawPoolEntry> Wallets { get; set; } = [];

    [JsonIgnore] public IReadOnlyList<PoolEntry> Pool { get; private set; } = [];

    public static SecretsFile Load(string path)
    {
        if (!File.Exists(path))
            throw new NightfallException(
                $"Secrets file not found: {path}. Copy tools/secrets.example.json and fill it in.");

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

        if (parsed.Nightfall is null)
            throw new NightfallException(
                $"{path}: the 'nightfall' section is missing. This tool now shares tools/secrets.json " +
                $"with spacestroker-fund-wallets and spacestroker-snipe-token — copy the 'nightfall' " +
                $"block from tools/secrets.example.json and fill it in.");

        var nightfall = parsed.Nightfall;

        if (IsPlaceholder(nightfall.Token.Mint))
            throw new NightfallException($"{path}: nightfall.token.mint is still a placeholder — fill in the Token CA.");

        WalletPool.ValidateAddress(nightfall.Token.Mint.Trim(), "nightfall.token.mint");
        nightfall.Token.Mint = nightfall.Token.Mint.Trim();

        if (nightfall.Jupiter.SlippageBps is < 0 or > 10_000)
            throw new NightfallException($"{path}: nightfall.jupiter.slippageBps must be between 0 and 10000.");

        if (nightfall.Jupiter.MaxPriceImpactPercent is < 0m or > 100m)
            throw new NightfallException($"{path}: nightfall.jupiter.maxPriceImpactPercent must be between 0 and 100.");

        if (nightfall.Sell.MaxConcurrentSells < 1)
            throw new NightfallException($"{path}: nightfall.sell.maxConcurrentSells must be at least 1.");

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

/// <summary>
/// spacestroker-nightfall's own slice of the shared secrets file. The `solana` and `wallets` keys
/// live at the TOP level because every tool reads them; everything in here is Nightfall's alone.
/// </summary>
public sealed class NightfallSection
{
    [JsonPropertyName("token")] public TokenSection Token { get; set; } = new();
    [JsonPropertyName("jupiter")] public JupiterSection Jupiter { get; set; } = new();
    [JsonPropertyName("sell")] public SellSection Sell { get; set; } = new();
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
    /// <summary>
    /// TTL of the in-memory quote cache (QuoteCache — no external store involved). A hover, and the
    /// confirmation dialog, reuse a quote for this long before Jupiter is called again. Raising it
    /// spends less quota but shows staler prices; lowering it does the reverse.
    /// </summary>
    [JsonPropertyName("quoteCacheSeconds")] public int QuoteCacheSeconds { get; set; } = 5;
}

public sealed class SellSection
{
    [JsonPropertyName("maxConcurrentSells")] public int MaxConcurrentSells { get; set; } = 4;
    [JsonPropertyName("minSolForFeeSol")] public decimal MinSolForFeeSol { get; set; } = 0.003m;
    [JsonPropertyName("refreshIntervalSeconds")] public int RefreshIntervalSeconds { get; set; } = 15;
    [JsonPropertyName("confirmTimeoutSeconds")] public int ConfirmTimeoutSeconds { get; set; } = 90;
    [JsonPropertyName("closeEmptiedTokenAccounts")] public bool CloseEmptiedTokenAccounts { get; set; } = true;
}
