using System.Text.Json;
using System.Text.Json.Serialization;
using SpaceStroker.TokenSniper.Domain;

namespace SpaceStroker.TokenSniper.Infrastructure;

/// <summary>
/// The unversioned local tools/secrets.json — SHARED with spacestroker-fund-wallets. This tool reads
/// the `solana` and `snipe` sections plus the pool's alias/PrivateKey/SupplyPercentage; the funder's
/// `coinbase`, `travelRule`, `funding`, `Label` and `SOLAmount` are ignored here, exactly as this
/// tool's fields are ignored there.
///
/// ⚠️ This file holds LIVE WALLET PRIVATE KEYS. It is git-ignored (tools/.gitignore) and nothing from
/// it beyond public addresses may reach console output, receipts or a report.
/// </summary>
public sealed class SecretsFile
{
    [JsonPropertyName("solana")] public SolanaSection Solana { get; set; } = new();
    [JsonPropertyName("snipe")] public SnipeSection Snipe { get; set; } = new();
    [JsonPropertyName("wallets")] public List<RawPoolEntry> Wallets { get; set; } = [];

    public static SecretsFile Load(string path)
    {
        if (!File.Exists(path))
            throw new SniperException(
                $"Secrets file not found: {path}. Copy tools/secrets.example.json and fill it in.");

        return Parse(File.ReadAllText(path), path);
    }

    public static SecretsFile Parse(string json, string path)
    {
        SecretsFile parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SecretsFile>(json, SnipePool.JsonOpts)
                     ?? throw new SniperException($"Could not parse {path}.");
        }
        catch (JsonException ex)
        {
            throw new SniperException($"Could not parse {path}: {ex.Message}");
        }

        if (IsPlaceholder(parsed.Solana.RpcUrl))
            throw new SniperException($"{path}: solana.rpcUrl is still a placeholder — fill in your keyed RPC endpoint.");

        if (string.IsNullOrWhiteSpace(parsed.Solana.RpcUrl))
            throw new SniperException($"{path}: solana.rpcUrl is required.");

        if (IsPlaceholder(parsed.Snipe.Mint) || string.IsNullOrWhiteSpace(parsed.Snipe.Mint))
            throw new SniperException($"{path}: snipe.mint is required — the token's contract address (CA).");

        if (!SnipePreflight.IsValidSolanaAddress(parsed.Snipe.Mint))
            throw new SniperException($"{path}: snipe.mint '{parsed.Snipe.Mint}' is not a valid Solana address.");

        if (parsed.Snipe.SlippageTolerance < 0)
            throw new SniperException($"{path}: snipe.slippageTolerance cannot be negative.");

        if (parsed.Snipe.SpreadMinutes < 0)
            throw new SniperException($"{path}: snipe.spreadMinutes cannot be negative.");

        foreach (var address in parsed.Snipe.ExtraAccounts)
            if (!SnipePreflight.IsValidSolanaAddress(address))
                throw new SniperException($"{path}: snipe.extraAccounts contains '{address}', which is not a valid Solana address.");

        return parsed;
    }

    public static bool IsPlaceholder(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains("REPLACE_ME", StringComparison.Ordinal);
}

public sealed class SolanaSection
{
    [JsonPropertyName("rpcUrl")] public string RpcUrl { get; set; } = "";
}

public sealed class SnipeSection
{
    [JsonPropertyName("mint")] public string Mint { get; set; } = "";
    [JsonPropertyName("spreadMinutes")] public int SpreadMinutes { get; set; } = 60;
    [JsonPropertyName("slippageTolerance")] public decimal SlippageTolerance { get; set; } = 0.25m;
    [JsonPropertyName("priorityFeeMicroLamports")] public ulong PriorityFeeMicroLamports { get; set; } = 200_000;
    [JsonPropertyName("computeUnitLimit")] public uint ComputeUnitLimit { get; set; } = 250_000;
    [JsonPropertyName("reserveLamports")] public ulong ReserveLamports { get; set; } = 15_000_000;
    [JsonPropertyName("confirmTimeoutSeconds")] public int ConfirmTimeoutSeconds { get; set; } = 90;
    [JsonPropertyName("balancePollSeconds")] public int BalancePollSeconds { get; set; } = 5;

    /// <summary>
    /// Optional trailing accounts appended after the 16 the IDL declares. Live buys carry two the IDL
    /// does not name; if a future deployment starts REQUIRING them, they go here without a code change.
    /// </summary>
    [JsonPropertyName("extraAccounts")] public List<string> ExtraAccounts { get; set; } = [];

    /// <summary>
    /// The IDL's third `buy` arg. null ⇒ omit it (the most common encoding in live traffic and the
    /// default), false ⇒ a single 0x00, true ⇒ 0x01 0x01. All three land on-chain today.
    /// </summary>
    [JsonPropertyName("trackVolume")] public bool? TrackVolume { get; set; }
}
