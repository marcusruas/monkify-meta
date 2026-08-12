namespace SpaceStroker.WalletFunder.Domain;

/// <summary>
/// Card #167 moved run state from tools/*.json into tools/Receipts/. This detects the one dangerous
/// consequence of that move.
///
/// ⚠️ The funder derives each wallet's Coinbase idempotency token from the plan, so a receipts file it
/// cannot see is indistinguishable from "no prior run" — and a re-run then issues a genuinely NEW
/// transfer rather than being deduplicated server-side. The pre-refactor file is deliberately left
/// where it is (relocating live run state automatically is its own hazard), so the tool must notice it
/// and stop.
///
/// IO-free on purpose: the caller does the two File.Exists calls, which keeps the money decision
/// unit-testable without a filesystem.
/// </summary>
public static class LegacyReceipts
{
    public static string? Describe(bool currentExists, bool legacyExists, string currentPath, string legacyPath)
    {
        if (currentExists || !legacyExists) return null;

        return $"Found a pre-refactor receipts file at {legacyPath} but nothing at {currentPath}. " +
               $"Move it there (or pass --receipts {legacyPath}) before running — starting fresh could " +
               $"re-send a transfer.";
    }
}
