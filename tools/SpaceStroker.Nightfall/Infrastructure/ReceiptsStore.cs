using System.Text.Json;
using System.Text.Json.Serialization;
using SpaceStroker.Nightfall.Domain;

namespace SpaceStroker.Nightfall.Infrastructure;

/// <summary>
/// Persists the <see cref="ReceiptsDocument"/>. Written after every state change, never only at the
/// end, so a crash mid-batch cannot lose a transaction signature.
///
/// ⚠️ Unlike spacestroker-fund-wallets there is NO plan hash and no resume gate. A sell is never
/// "resumed": the wallet either still holds tokens (sell again) or it does not (nothing to do). The
/// chain is the resume state; this file is the audit trail. Do not port MatchesPlan across.
/// </summary>
public sealed class ReceiptsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ReceiptsDocument _document;

    private ReceiptsStore(string path, ReceiptsDocument document)
    {
        Path = path;
        _document = document;
    }

    public string Path { get; }

    public IReadOnlyList<SellReceipt> Entries => _document.Entries;

    public static ReceiptsStore LoadOrCreate(string path, string tokenMint)
    {
        ReceiptsDocument document;

        if (File.Exists(path))
        {
            try
            {
                document = JsonSerializer.Deserialize<ReceiptsDocument>(File.ReadAllText(path), JsonOpts)
                           ?? new ReceiptsDocument();
            }
            catch (JsonException ex)
            {
                throw new NightfallException($"Could not parse the receipts file {path}: {ex.Message}");
            }

            // A different token means these receipts describe a different operation entirely.
            // Start a fresh document rather than interleaving two tokens in one audit trail.
            if (!string.IsNullOrEmpty(document.TokenMint) && document.TokenMint != tokenMint)
                document = new ReceiptsDocument();
        }
        else
        {
            document = new ReceiptsDocument();
        }

        document.TokenMint = tokenMint;

        return new ReceiptsStore(path, document);
    }

    /// <summary>Upsert then persist immediately — never batch these.</summary>
    public void Record(SellReceipt receipt)
    {
        _document.Upsert(receipt);
        Save();
    }

    private void Save()
    {
        _document.UpdatedUtc = DateTimeOffset.UtcNow;

        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllText(Path, JsonSerializer.Serialize(_document, JsonOpts));
    }
}
