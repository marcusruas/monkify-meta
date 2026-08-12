namespace SpaceStroker.Nightfall.Domain;

public enum SellErrorClass
{
    /// <summary>Worth another attempt with backoff — the batch carries on regardless.</summary>
    Retryable,

    /// <summary>A real answer we should show the operator. The wallet stays re-selectable.</summary>
    Terminal,
}

/// <summary>
/// The single place that decides whether a failed sell is worth retrying. Concentrated here (rather
/// than at each call site) so the policy is one unit-tested table instead of scattered string checks.
/// </summary>
public static class SellError
{
    private static readonly string[] RetryableMessages =
    [
        "blockhash not found",
        "block height exceeded",
        "was not confirmed",
        "timed out",
        "timeout",
    ];

    private static readonly string[] TerminalMessages =
    [
        "slippage",
        "route",
        "insufficient funds",
        "insufficient lamports",
        "0x1771",   // Jupiter's slippage-tolerance-exceeded custom program error
    ];

    public static SellErrorClass Classify(int? httpStatus, string? message, bool isTransport)
    {
        if (isTransport)
            return SellErrorClass.Retryable;

        var text = (message ?? string.Empty).ToLowerInvariant();

        // A business rejection wins over the status code: Jupiter answers "no route" with a 400,
        // and retrying it just spends rate limit to be told the same thing.
        if (TerminalMessages.Any(text.Contains))
            return SellErrorClass.Terminal;

        if (RetryableMessages.Any(text.Contains))
            return SellErrorClass.Retryable;

        if (httpStatus is int status)
            return status == 429 || status >= 500 ? SellErrorClass.Retryable : SellErrorClass.Terminal;

        // Fail closed. An unrecognised error retried in a loop against a money path is worse than
        // stopping and showing the operator exactly what came back.
        return SellErrorClass.Terminal;
    }
}
