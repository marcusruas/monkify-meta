using System.Security.Cryptography;
using System.Text;

namespace SpaceStroker.WalletFunder;

public sealed record PlannedTransfer(string Label, string Address, decimal SolAmount, long Lamports, Guid Idem);

/// <summary>
/// The immutable funding plan. The PLAN HASH covers exactly what a resend would change — addresses and
/// amounts — and nothing that must not invalidate a resume (authored order, labels, formatting).
/// </summary>
public sealed class FundingPlan
{
    public required string PlanHash { get; init; }

    public required IReadOnlyList<PlannedTransfer> Transfers { get; init; }

    public decimal TotalSol => Transfers.Sum(t => t.SolAmount);

    public static FundingPlan Build(IReadOnlyList<PoolEntry> pool)
    {
        var lamports = pool.ToDictionary(e => e, e => WalletPool.ToLamports(e.SolAmount));

        // Canonical form: sorted by address (ordinal) so re-ordering the file is not a plan change.
        var canonical = new StringBuilder();
        foreach (var entry in pool.OrderBy(e => e.PublicKey, StringComparer.Ordinal))
            canonical.Append(entry.PublicKey).Append(':').Append(lamports[entry]).Append(';');

        var planHash = Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();

        var transfers = pool
            .Select(e => new PlannedTransfer(
                e.Label, e.PublicKey, e.SolAmount, lamports[e],
                DeriveIdem(planHash, e.PublicKey, lamports[e])))
            .ToList();

        return new FundingPlan { PlanHash = planHash, Transfers = transfers };
    }

    /// <summary>
    /// Deterministic, UUIDv4-SHAPED idempotency token. Coinbase asks for a UUIDv4; deriving it from the
    /// plan means a repeat run reuses the same token and Coinbase deduplicates SERVER-SIDE even when the
    /// local receipts file is gone. Change an amount or address and the token changes — which is a real
    /// new send, and exactly why Receipts guards against plan drift.
    /// </summary>
    public static Guid DeriveIdem(string planHash, string address, long lamports)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{planHash}|{address}|{lamports}"));

        // Build the canonical UUID text directly: Guid's byte[] constructor reads the first three groups
        // little-endian, so stamping version/variant on raw bytes lands them in the wrong nibbles.
        var hex = Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant();
        var version = '4';                                    // RFC 4122 version 4
        var variant = "89ab"[digest[8] & 0x03];               // RFC 4122 variant (10xx)

        var text = $"{hex[..8]}-{hex[8..12]}-{version}{hex[13..16]}-{variant}{hex[17..20]}-{hex[20..32]}";
        return Guid.Parse(text);
    }
}
