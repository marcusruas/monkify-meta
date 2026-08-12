using System.Text.Json.Serialization;

namespace SpaceStroker.Nightfall.Domain;

public enum SellStatus
{
    Planned,
    Quoted,
    Simulated,
    Sent,
    Sold,
    Unconfirmed,
    Failed,
}

/// <summary>
/// One wallet's line in the audit trail.
///
/// ⚠️ Deliberately carries NO private key and no secrets — this type is serialized straight to
/// nightfall-receipts.json, which lives on disk next to the operator's other files.
/// </summary>
public sealed class SellReceipt
{
    public string Alias { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string BatchId { get; set; } = "";

    /// <summary>Null means the operator left SELL % blank, i.e. 100%.</summary>
    public decimal? Percent { get; set; }

    public string TokenAmountRaw { get; set; } = "0";
    public decimal TokenAmount { get; set; }

    /// <summary>What Jupiter promised.</summary>
    public decimal? QuotedSol { get; set; }

    /// <summary>What the chain actually paid. The gap against QuotedSol is the slippage.</summary>
    public decimal? RealizedSol { get; set; }

    public string? Signature { get; set; }

    /// <summary>The follow-up close transaction, when a 100% sell reclaimed the account rent.</summary>
    public string? CloseSignature { get; set; }

    public SellStatus Status { get; set; } = SellStatus.Planned;
    public string? Error { get; set; }
    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SettledUtc { get; set; }

    private static readonly Dictionary<SellStatus, SellStatus[]> Allowed = new()
    {
        [SellStatus.Planned] = [SellStatus.Quoted, SellStatus.Failed],
        [SellStatus.Quoted] = [SellStatus.Simulated, SellStatus.Sent, SellStatus.Failed],
        [SellStatus.Simulated] = [SellStatus.Planned, SellStatus.Quoted],
        [SellStatus.Sent] = [SellStatus.Sold, SellStatus.Unconfirmed, SellStatus.Failed],
        [SellStatus.Sold] = [],
        [SellStatus.Unconfirmed] = [SellStatus.Sold, SellStatus.Failed],
        [SellStatus.Failed] = [SellStatus.Planned, SellStatus.Quoted],
    };

    /// <summary>
    /// A settled sale is never walked backwards. The receipt is what an operator reconciles against,
    /// and a Sold row reverting to Sent is an invitation to sell the same position twice.
    /// </summary>
    public void MoveTo(SellStatus next)
    {
        if (!Allowed[Status].Contains(next))
            throw new NightfallException($"A sell cannot move from {Status} to {next}.");

        Status = next;

        if (next is SellStatus.Sold or SellStatus.Failed or SellStatus.Simulated)
            SettledUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Test/deserialization seam — bypasses the transition rules on purpose.</summary>
    internal void ForceStatus(SellStatus status) => Status = status;
}

public sealed class ReceiptsDocument
{
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string TokenMint { get; set; } = "";
    public List<SellReceipt> Entries { get; set; } = [];

    [JsonIgnore]
    public IReadOnlyList<SellReceipt> Rows => Entries;

    /// <summary>One row per wallet — the latest state replaces the previous one.</summary>
    public void Upsert(SellReceipt receipt)
    {
        var index = Entries.FindIndex(e => e.PublicKey == receipt.PublicKey);

        if (index >= 0)
            Entries[index] = receipt;
        else
            Entries.Add(receipt);
    }
}
