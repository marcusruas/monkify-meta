namespace SpaceStroker.Seeder;

/// <summary>Where a pass is relative to the one irreversible act: sending the stake transfer.</summary>
public enum RetryPhase
{
    /// <summary>No transfer has been sent yet. Nothing has cost money — retry freely.</summary>
    PreSend,

    /// <summary>A transfer signature exists. The stake is gone; the transfer must never repeat.</summary>
    PostSend,
}

/// <summary>The distinct things a pass does, so the phase gate can reason about them by name.</summary>
public enum SeederOperation
{
    Discovery,
    SendTransfer,
    ConfirmSignature,
    PlaceBet,
}

/// <summary>
/// Retry policy. Pure: the caller supplies the Random and does the actual sleeping.
///
/// THE MONEY RULE lives in <see cref="IsRetryable"/>: once a transfer signature exists,
/// SendTransfer is never retryable again. A re-send would spend a second stake for a single bet,
/// and nothing downstream could tell the difference.
/// </summary>
public static class RetrySchedule
{
    private const double JitterFraction = 0.2;

    /// <summary>
    /// Transient = worth trying again shortly. A NULL status means no response arrived at all
    /// (connection reset, DNS failure, timeout) — the most common transient case, and the one a
    /// status-only classifier would miss entirely. A Cloud Armor denial also lands here.
    /// </summary>
    public static bool IsTransient(int? httpStatus)
        => httpStatus is null or 408 or 429 or (>= 500 and <= 599);

    /// <summary>
    /// Exponential back-off with ±20% jitter. Jitter matters because several clients behind one
    /// NAT/VPN exit would otherwise retry in lockstep and re-trip the same rate limit together.
    /// </summary>
    public static IReadOnlyList<TimeSpan> Delays(int attempts, int baseDelaySeconds, Random random)
    {
        var count = Math.Max(0, attempts);
        var delays = new List<TimeSpan>(count);

        for (var i = 0; i < count; i++)
        {
            var centre = baseDelaySeconds * Math.Pow(2, i);
            var jitter = 1.0 + ((random.NextDouble() * 2.0 - 1.0) * JitterFraction);
            delays.Add(TimeSpan.FromSeconds(centre * jitter));
        }

        return delays;
    }

    public static bool IsRetryable(RetryPhase phase, SeederOperation operation) => phase switch
    {
        RetryPhase.PreSend => operation is SeederOperation.Discovery or SeederOperation.SendTransfer,

        // Polling is read-only, and re-POSTing the SAME signature is rejected by the backend as
        // PaymentSignatureHasBeenUsed — so both are safe. SendTransfer is not, and never will be.
        RetryPhase.PostSend => operation is SeederOperation.ConfirmSignature or SeederOperation.PlaceBet,

        _ => false,
    };
}
