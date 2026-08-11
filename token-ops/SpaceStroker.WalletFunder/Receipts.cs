using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceStroker.WalletFunder;

public enum TransferStatus
{
    Planned,
    Sent,
    Pending,
    Confirmed,
    Unconfirmed,
    Failed,
}

public sealed class ReceiptEntry
{
    public string Label { get; set; } = "";
    public string Address { get; set; } = "";
    public decimal AmountSol { get; set; }
    public long Lamports { get; set; }
    public string Idem { get; set; } = "";
    public string? CoinbaseTransactionId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TransferStatus Status { get; set; } = TransferStatus.Planned;

    public bool ChainConfirmed { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? SentUtc { get; set; }
    public DateTimeOffset? SettledUtc { get; set; }
}

public sealed class ReceiptsFile
{
    public string PlanHash { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public List<ReceiptEntry> Entries { get; set; } = [];
}

/// <summary>
/// The receipts file IS the resume state — written after every state change, never only at the end,
/// so a crash cannot lose a Coinbase transaction id.
/// </summary>
public sealed class Receipts
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ReceiptsFile _file;

    private Receipts(string path, ReceiptsFile file)
    {
        Path = path;
        _file = file;
    }

    public string Path { get; }

    public IReadOnlyList<ReceiptEntry> Entries => _file.Entries;

    public bool AllConfirmed => _file.Entries.All(e => e.Status == TransferStatus.Confirmed);

    public static Receipts LoadOrCreate(string path, FundingPlan plan, bool forcePlanChange)
    {
        ReceiptsFile file;

        if (File.Exists(path))
        {
            file = JsonSerializer.Deserialize<ReceiptsFile>(File.ReadAllText(path), JsonOpts)
                   ?? throw new FunderException($"Could not parse the receipts file {path}.");

            if (!string.Equals(file.PlanHash, plan.PlanHash, StringComparison.OrdinalIgnoreCase))
            {
                if (!forcePlanChange)
                    throw new FunderException(
                        $"The wallet pool changed since these receipts were written.{Environment.NewLine}" +
                        $"  receipts plan: {file.PlanHash}{Environment.NewLine}" +
                        $"  current plan:  {plan.PlanHash}{Environment.NewLine}" +
                        "Each wallet's idempotency token is derived from the plan, so the changed entries would be " +
                        "sent AGAIN rather than deduplicated by Coinbase. Re-run with --force-plan-change only if " +
                        "you accept that risk.");

                file = NewFile(plan);
            }
            else
            {
                Reconcile(file, plan);
            }
        }
        else
        {
            file = NewFile(plan);
        }

        var receipts = new Receipts(path, file);
        receipts.Save();
        return receipts;
    }

    public ReceiptEntry For(PlannedTransfer transfer)
        => _file.Entries.FirstOrDefault(e => string.Equals(e.Address, transfer.Address, StringComparison.Ordinal))
           ?? throw new FunderException($"No receipt entry for {transfer.Address}.");

    public void Update(ReceiptEntry entry, Action<ReceiptEntry> mutate)
    {
        mutate(entry);
        Save();
    }

    /// <summary>A wallet already holding a Coinbase transaction id is POLLED, never re-sent.</summary>
    public static bool NeedsSend(ReceiptEntry entry)
        => string.IsNullOrWhiteSpace(entry.CoinbaseTransactionId) && entry.Status != TransferStatus.Confirmed;

    public IReadOnlyList<PlannedTransfer> Outstanding(FundingPlan plan)
        => plan.Transfers.Where(t => For(t).Status != TransferStatus.Confirmed).ToList();

    private static ReceiptsFile NewFile(FundingPlan plan) => new()
    {
        PlanHash = plan.PlanHash,
        CreatedUtc = DateTimeOffset.UtcNow,
        UpdatedUtc = DateTimeOffset.UtcNow,
        Entries = plan.Transfers.Select(Seed).ToList(),
    };

    private static ReceiptEntry Seed(PlannedTransfer transfer) => new()
    {
        Label = transfer.Label,
        Address = transfer.Address,
        AmountSol = transfer.SolAmount,
        Lamports = transfer.Lamports,
        Idem = transfer.Idem.ToString("D"),
        Status = TransferStatus.Planned,
    };

    /// <summary>Same plan hash but an entry missing (hand-edited file) => seed it rather than crash.</summary>
    private static void Reconcile(ReceiptsFile file, FundingPlan plan)
    {
        foreach (var transfer in plan.Transfers)
            if (file.Entries.All(e => !string.Equals(e.Address, transfer.Address, StringComparison.Ordinal)))
                file.Entries.Add(Seed(transfer));
    }

    private void Save()
    {
        _file.UpdatedUtc = DateTimeOffset.UtcNow;

        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllText(Path, JsonSerializer.Serialize(_file, JsonOpts));
    }
}
