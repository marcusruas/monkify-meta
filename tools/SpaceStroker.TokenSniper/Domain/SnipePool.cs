using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceStroker.TokenSniper.Domain;

/// <summary>
/// One ghost wallet. Unlike the funder's <c>PoolEntry</c>, this one DOES carry the private key —
/// the sniper is the only tool in the solution that ever binds one. It must never reach console
/// output, receipts or a report.
/// </summary>
public sealed record PoolEntry(string Alias, string PublicKey, string PrivateKey, decimal SupplyPercentage);

/// <summary>JSON binding shape. Shared verbatim with spacestroker-fund-wallets' pool file.</summary>
public sealed class RawPoolEntry
{
    [JsonPropertyName("alias")] public string? Alias { get; set; }
    [JsonPropertyName("PublicKey")] public string? PublicKey { get; set; }
    [JsonPropertyName("PrivateKey")] public string? PrivateKey { get; set; }
    [JsonPropertyName("SupplyPercentage")] public decimal SupplyPercentage { get; set; }
}

public static class SnipePool
{
    public const int TokenDecimalsDefault = 6;

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Unknown members are ignored (the default). Do NOT set UnmappedMemberHandling.Disallow:
        // the pool file is SHARED with spacestroker-fund-wallets, so Label/SOLAmount ride along here
        // exactly as alias/PrivateKey/SupplyPercentage ride along there.
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
            throw new SniperException($"Could not parse the wallet pool: {ex.Message}");
        }

        if (raw is null) throw new SniperException("The wallet pool is empty or null.");

        return FromEntries(raw);
    }

    /// <summary>
    /// ⚠️ Unlike the funder's WalletPool.FromEntries, a blank alias is NOT replaced with a synthesised
    /// "W01". The alias is this tool's ORDERING KEY, so inventing one would silently reorder the run —
    /// SnipePreflight rejects it instead.
    /// </summary>
    public static IReadOnlyList<PoolEntry> FromEntries(IEnumerable<RawPoolEntry> raw) =>
        raw.Select(entry => new PoolEntry(
                (entry.Alias ?? "").Trim(),
                (entry.PublicKey ?? "").Trim(),
                (entry.PrivateKey ?? "").Trim(),
                entry.SupplyPercentage))
            .ToList();

    /// <summary>Base units to a display string. Invariant — a pt-BR machine would otherwise emit "2,54".</summary>
    public static string FormatTokens(ulong baseUnits, int decimals)
    {
        var scale = Math.Pow(10, decimals);
        return (baseUnits / (decimal)scale).ToString("0.######", CultureInfo.InvariantCulture);
    }

    public static string FormatSol(ulong lamports) =>
        (lamports / 1_000_000_000m).ToString("0.000000000", CultureInfo.InvariantCulture);

    public static string FormatPercent(decimal percent) =>
        percent.ToString("0.####", CultureInfo.InvariantCulture);
}
