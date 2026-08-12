using System.Text.Json;

namespace SpaceStroker.Nightfall.Infrastructure.Jupiter;

/// <summary>
/// A Jupiter quote.
///
/// <see cref="Raw"/> is the response echoed back VERBATIM to /swap — Jupiter's route plan is opaque
/// and versioned, so re-serializing it from typed fields would silently drop whatever we did not
/// model and produce a different (or invalid) swap.
/// </summary>
public sealed record JupiterQuote(
    string InputMint,
    string OutputMint,
    ulong InAmount,
    ulong OutAmount,
    decimal PriceImpactPercent,
    JsonElement Raw)
{
    /// <summary>Expected SOL out, for display and for the confirmation dialog's batch total.</summary>
    public decimal OutSol => OutAmount / 1_000_000_000m;
}
