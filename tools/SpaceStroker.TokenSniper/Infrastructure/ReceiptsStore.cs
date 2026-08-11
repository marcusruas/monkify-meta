using System.Text.Json;
using System.Text.Json.Serialization;
using SpaceStroker.TokenSniper.Domain;

namespace SpaceStroker.TokenSniper.Infrastructure;

/// <summary>
/// Persists the run as it happens.
///
/// ⚠️ Unlike the funder's receipts file, this one is an AUDIT TRAIL, not the resume authority. Solana
/// has no server-side idempotency token, so a receipts-driven resume could re-buy a wallet whose
/// transaction confirmed just before a crash. The resume authority is the wallet's on-chain token
/// balance; this file exists for the report and for the signatures.
/// </summary>
public sealed class ReceiptsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly RunReport _report;

    public ReceiptsStore(string path, RunReport report)
    {
        Path = path;
        _report = report;
    }

    public string Path { get; }

    public static RunReport? TryLoad(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<RunReport>(File.ReadAllText(path), JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new SniperException(
                $"Could not parse the receipts file {path}: {ex.Message}. Move it aside and re-run — the " +
                "resume is driven by on-chain balances, so nothing is lost by discarding it.");
        }
    }

    /// <summary>Write after EVERY state change, never batched — a crash must not lose a signature.</summary>
    public void Save()
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllText(Path, JsonSerializer.Serialize(_report, JsonOpts));
    }
}
