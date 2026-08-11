using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceStroker.WalletFunder.Domain;

/// <summary>
/// One funding target. Deliberately carries NO private-key member: card #63's ghost-wallet pool
/// entries include a PrivateKey this tool must never bind, hold or log.
/// </summary>
public sealed record PoolEntry(string Label, string PublicKey, decimal SolAmount);

/// <summary>JSON binding shape. Shared by --pool files and secrets.json's "wallets" array.</summary>
public sealed class RawPoolEntry
{
    [JsonPropertyName("Label")] public string? Label { get; set; }
    [JsonPropertyName("PublicKey")] public string? PublicKey { get; set; }
    [JsonPropertyName("SOLAmount")] public decimal SOLAmount { get; set; }
}

public static class WalletPool
{
    public const long LamportsPerSol = 1_000_000_000L;

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Unknown members are ignored (the default). Do NOT set UnmappedMemberHandling.Disallow:
        // #63's pool file carries PrivateKey/SupplyPercentage and must load unchanged.
    };

    public static IReadOnlyList<PoolEntry> Parse(string json)
    {
        List<RawPoolEntry>? raw;
        try
        {
            raw = JsonSerializer.Deserialize<List<RawPoolEntry>>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new FunderException($"Could not parse the wallet pool: {ex.Message}");
        }

        if (raw is null) throw new FunderException("The wallet pool is empty or null.");

        return FromEntries(raw);
    }

    public static IReadOnlyList<PoolEntry> FromEntries(IEnumerable<RawPoolEntry> raw)
    {
        var result = new List<PoolEntry>();
        var index = 0;

        foreach (var entry in raw)
        {
            index++;
            var label = string.IsNullOrWhiteSpace(entry.Label) ? $"W{index:00}" : entry.Label.Trim();
            var address = (entry.PublicKey ?? "").Trim();
            result.Add(new PoolEntry(label, address, entry.SOLAmount));
        }

        return result;
    }

    /// <summary>
    /// SOL to integer lamports. Integer math is the comparison and hashing unit throughout;
    /// amounts are decimal in, never double.
    /// </summary>
    public static long ToLamports(decimal sol)
    {
        var scaled = sol * LamportsPerSol;
        if (scaled != decimal.Truncate(scaled))
            throw new FunderException($"Amount {FormatSol(sol)} SOL is finer than one lamport.");

        return (long)scaled;
    }

    /// <summary>
    /// Invariant decimal rendering for the outbound Coinbase "amount" string. Explicit even though the
    /// project sets InvariantGlobalization — a pt-BR machine would otherwise emit "2,54".
    /// </summary>
    public static string FormatSol(decimal sol)
        => sol.ToString("0.#########", CultureInfo.InvariantCulture);
}
