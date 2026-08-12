using System.Text.Json;
using SpaceStroker.WalletFunder.Domain;

namespace SpaceStroker.WalletFunder.Infrastructure;

/// <summary>
/// Persists the <see cref="ReceiptsDocument"/>. The file IS the resume state — written after every state
/// change, never only at the end, so a crash cannot lose a Coinbase transaction id.
/// The rules it enforces live in the domain document; this type only owns the disk.
/// </summary>
public sealed class ReceiptsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ReceiptsDocument _document;

    private ReceiptsStore(string path, ReceiptsDocument document)
    {
        Path = path;
        _document = document;
    }

    public string Path { get; }

    public IReadOnlyList<ReceiptEntry> Entries => _document.Entries;

    public bool AllConfirmed => _document.AllConfirmed;

    public static ReceiptsStore LoadOrCreate(string path, FundingPlan plan, bool forcePlanChange)
    {
        ReceiptsDocument document;

        if (File.Exists(path))
        {
            document = JsonSerializer.Deserialize<ReceiptsDocument>(File.ReadAllText(path), JsonOpts)
                       ?? throw new FunderException($"Could not parse the receipts file {path}.");

            if (!document.MatchesPlan(plan))
            {
                if (!forcePlanChange)
                    throw new FunderException(
                        $"The wallet pool changed since these receipts were written.{Environment.NewLine}" +
                        $"  receipts plan: {document.PlanHash}{Environment.NewLine}" +
                        $"  current plan:  {plan.PlanHash}{Environment.NewLine}" +
                        "Each wallet's idempotency token is derived from the plan, so the changed entries would be " +
                        "sent AGAIN rather than deduplicated by Coinbase. Re-run with --force-plan-change only if " +
                        "you accept that risk.");

                document = ReceiptsDocument.ForPlan(plan);
            }
            else
            {
                document.Reconcile(plan);
            }
        }
        else
        {
            document = ReceiptsDocument.ForPlan(plan);
        }

        var store = new ReceiptsStore(path, document);
        store.Save();
        return store;
    }

    public ReceiptEntry For(PlannedTransfer transfer) => _document.For(transfer);

    public IReadOnlyList<PlannedTransfer> Outstanding(FundingPlan plan) => _document.Outstanding(plan);

    /// <summary>Mutate then persist immediately — never batch these.</summary>
    public void Update(ReceiptEntry entry, Action<ReceiptEntry> mutate)
    {
        mutate(entry);
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
