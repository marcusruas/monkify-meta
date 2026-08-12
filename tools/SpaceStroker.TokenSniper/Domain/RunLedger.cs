using System.Text.Json.Serialization;

namespace SpaceStroker.TokenSniper.Domain;

public enum WalletStatus
{
    Pending,
    Filled,
    Short,
    Skipped,
    Failed,
    Unconfirmed,
}

/// <summary>
/// One wallet's result. ⚠️ There is deliberately NO private-key member: the report types cannot leak
/// what they never carry.
/// </summary>
public sealed class WalletOutcome
{
    [JsonPropertyName("alias")] public string Alias { get; set; } = "";
    [JsonPropertyName("publicKey")] public string PublicKey { get; set; } = "";
    [JsonPropertyName("targetPercent")] public decimal TargetPercent { get; set; }
    [JsonPropertyName("achievedPercent")] public decimal AchievedPercent { get; set; }
    [JsonPropertyName("targetBaseUnits")] public ulong TargetBaseUnits { get; set; }
    [JsonPropertyName("heldBeforeBaseUnits")] public ulong HeldBeforeBaseUnits { get; set; }
    [JsonPropertyName("tokensReceivedBaseUnits")] public ulong TokensReceivedBaseUnits { get; set; }
    [JsonPropertyName("heldAfterBaseUnits")] public ulong HeldAfterBaseUnits { get; set; }
    [JsonPropertyName("solSpentLamports")] public ulong SolSpentLamports { get; set; }
    [JsonPropertyName("averagePriceLamportsPerToken")] public decimal AveragePriceLamportsPerToken { get; set; }
    [JsonPropertyName("signature")] public string? Signature { get; set; }
    [JsonPropertyName("status")] public WalletStatus Status { get; set; } = WalletStatus.Pending;
    [JsonPropertyName("note")] public string? Note { get; set; }
    [JsonPropertyName("scheduledOffsetSeconds")] public double ScheduledOffsetSeconds { get; set; }
    [JsonPropertyName("timestampUtc")] public DateTimeOffset? TimestampUtc { get; set; }
}

/// <summary>The whole run, as written to both artifacts and to the receipts file.</summary>
public sealed class RunReport
{
    [JsonPropertyName("mint")] public string Mint { get; set; } = "";
    [JsonPropertyName("dryRun")] public bool DryRun { get; set; }
    [JsonPropertyName("startedUtc")] public DateTimeOffset StartedUtc { get; set; }
    [JsonPropertyName("finishedUtc")] public DateTimeOffset? FinishedUtc { get; set; }
    [JsonPropertyName("mintTotalSupplyBaseUnits")] public ulong MintTotalSupplyBaseUnits { get; set; }
    [JsonPropertyName("tokenDecimals")] public int TokenDecimals { get; set; } = SnipePool.TokenDecimalsDefault;
    [JsonPropertyName("stoppedReason")] public string? StoppedReason { get; set; }
    [JsonPropertyName("wallets")] public List<WalletOutcome> Wallets { get; set; } = [];

    [JsonPropertyName("poolPercentOfSupply")] public decimal PoolPercentOfSupply { get; set; }
    [JsonPropertyName("totalSolSpentLamports")] public ulong TotalSolSpentLamports { get; set; }
    [JsonPropertyName("totalTokensReceivedBaseUnits")] public ulong TotalTokensReceivedBaseUnits { get; set; }
    [JsonPropertyName("blendedAveragePriceLamportsPerToken")] public decimal BlendedAveragePriceLamportsPerToken { get; set; }
}

/// <summary>
/// Accumulates outcomes and derives the run totals. Pure: the caller feeds it observed on-chain
/// values, it owns no clock and no disk.
/// </summary>
public sealed class RunLedger
{
    private readonly RunReport _report;

    public RunLedger(RunReport report) => _report = report;

    public RunReport Report => _report;

    public WalletOutcome For(string alias) =>
        _report.Wallets.FirstOrDefault(w => w.Alias == alias)
        ?? throw new SniperException($"No outcome row for wallet '{alias}'.");

    public void Recalculate()
    {
        foreach (var wallet in _report.Wallets)
        {
            wallet.AchievedPercent = _report.MintTotalSupplyBaseUnits == 0
                ? 0
                : decimal.Round((decimal)wallet.HeldAfterBaseUnits * 100m / _report.MintTotalSupplyBaseUnits, 6);

            wallet.AveragePriceLamportsPerToken = wallet.TokensReceivedBaseUnits == 0
                ? 0
                : decimal.Round((decimal)wallet.SolSpentLamports / wallet.TokensReceivedBaseUnits, 12);
        }

        _report.TotalSolSpentLamports = _report.Wallets.Aggregate(0ul, (sum, w) => sum + w.SolSpentLamports);
        _report.TotalTokensReceivedBaseUnits = _report.Wallets.Aggregate(0ul, (sum, w) => sum + w.TokensReceivedBaseUnits);

        var held = _report.Wallets.Aggregate(0ul, (sum, w) => sum + w.HeldAfterBaseUnits);
        _report.PoolPercentOfSupply = _report.MintTotalSupplyBaseUnits == 0
            ? 0
            : decimal.Round((decimal)held * 100m / _report.MintTotalSupplyBaseUnits, 6);

        _report.BlendedAveragePriceLamportsPerToken = _report.TotalTokensReceivedBaseUnits == 0
            ? 0
            : decimal.Round((decimal)_report.TotalSolSpentLamports / _report.TotalTokensReceivedBaseUnits, 12);
    }

    /// <summary>
    /// 0 only when every targeted wallet reached its target (or was already there), or the run was a
    /// dry run / empty pool. Anything short, failed or unresolved is a non-zero exit — the same
    /// contract as the funder.
    /// </summary>
    public int ExitCode =>
        _report.DryRun || _report.Wallets.Count == 0
            ? 0
            : _report.Wallets.All(w => w.Status is WalletStatus.Filled or WalletStatus.Skipped) &&
              _report.StoppedReason is null
                ? 0
                : 1;
}
