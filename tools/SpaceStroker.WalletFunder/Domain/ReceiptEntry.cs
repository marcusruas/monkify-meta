using System.Text.Json.Serialization;

namespace SpaceStroker.WalletFunder.Domain;

public enum TransferStatus
{
    Planned,
    Sent,
    Pending,
    Confirmed,
    Unconfirmed,
    Failed,
}

/// <summary>One wallet's line in the run record. Carries no credential material, ever.</summary>
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

/// <summary>
/// The receipts document and the rules that read it — resume filtering, the never-re-send rule and the
/// plan-drift check. Pure: no file IO lives here (see <c>Infrastructure.ReceiptsStore</c>), which is what
/// lets the money-safety rules be unit-tested without touching Coinbase or the disk.
/// </summary>
public sealed class ReceiptsDocument
{
    public string PlanHash { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public List<ReceiptEntry> Entries { get; set; } = [];

    public bool AllConfirmed => Entries.All(e => e.Status == TransferStatus.Confirmed);

    public static ReceiptsDocument ForPlan(FundingPlan plan) => new()
    {
        PlanHash = plan.PlanHash,
        CreatedUtc = DateTimeOffset.UtcNow,
        UpdatedUtc = DateTimeOffset.UtcNow,
        Entries = plan.Transfers.Select(Seed).ToList(),
    };

    public bool MatchesPlan(FundingPlan plan)
        => string.Equals(PlanHash, plan.PlanHash, StringComparison.OrdinalIgnoreCase);

    /// <summary>Same plan hash but an entry missing (hand-edited file) => seed it rather than crash.</summary>
    public void Reconcile(FundingPlan plan)
    {
        foreach (var transfer in plan.Transfers)
            if (Entries.All(e => !string.Equals(e.Address, transfer.Address, StringComparison.Ordinal)))
                Entries.Add(Seed(transfer));
    }

    public ReceiptEntry For(PlannedTransfer transfer)
        => Entries.FirstOrDefault(e => string.Equals(e.Address, transfer.Address, StringComparison.Ordinal))
           ?? throw new FunderException($"No receipt entry for {transfer.Address}.");

    /// <summary>Everything not yet confirmed — what a resume still has work to do on.</summary>
    public IReadOnlyList<PlannedTransfer> Outstanding(FundingPlan plan)
        => plan.Transfers.Where(t => For(t).Status != TransferStatus.Confirmed).ToList();

    /// <summary>A wallet already holding a Coinbase transaction id is POLLED, never re-sent.</summary>
    public static bool NeedsSend(ReceiptEntry entry)
        => string.IsNullOrWhiteSpace(entry.CoinbaseTransactionId) && entry.Status != TransferStatus.Confirmed;

    private static ReceiptEntry Seed(PlannedTransfer transfer) => new()
    {
        Label = transfer.Label,
        Address = transfer.Address,
        AmountSol = transfer.SolAmount,
        Lamports = transfer.Lamports,
        Idem = transfer.Idem.ToString("D"),
        Status = TransferStatus.Planned,
    };
}
