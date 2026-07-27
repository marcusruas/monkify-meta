namespace SpaceStroker.Deploy;

/// <summary>
/// The pure decisions behind `teardown`: which Terraform address must have its deletion-protection
/// cleared, how many destroy passes to run, and how to classify whatever survives. No I/O — every
/// method takes already-captured command output and returns a value, which is what makes the teardown
/// logic testable at all (<see cref="Deployer"/> keeps every process call).
/// </summary>
public static class TeardownDecisions
{
    /// <summary>
    /// The only resource whose deletion_protection blocks `destroy`. Note this is the PROVIDER-level
    /// guard (not settings.deletion_protection_enabled), so flipping it is a state-only update —
    /// a targeted apply on it creates and mutates nothing in GCP.
    /// </summary>
    public const string SqlInstanceAddress = "module.data.google_sql_database_instance.postgres";

    /// <summary>
    /// VPC + subnet + private-services range. All zero-cost, and the trio held hostage by the
    /// Google-managed serverless-ipv4 address reservation long after Cloud Run is gone.
    /// </summary>
    public const string ZeroCostPrefix = "module.network.";

    /// <summary>
    /// Waits BETWEEN destroy passes; the count implies the number of passes. The three known ordering
    /// failures each clear once the resource that was blocking them is really gone, so a blind re-pass
    /// is both correct and cheaper than parsing GCP's error text.
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> DestroyBackoff =
        [TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120)];

    /// <summary>Total destroy passes: one more than the number of waits between them.</summary>
    public static int DestroyAttempts => DestroyBackoff.Count + 1;

    /// <summary>Split `terraform state list` stdout into trimmed, non-empty resource addresses.</summary>
    public static IReadOnlyList<string> ParseStateList(string stdout) =>
        (stdout ?? "")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

    /// <summary>Exact-match lookup — a longer address sharing the prefix is a different resource.</summary>
    public static bool Contains(IReadOnlyList<string> addresses, string address) =>
        addresses.Any(a => string.Equals(a, address, StringComparison.Ordinal));

    /// <summary>
    /// True when SOMETHING survived and every survivor is zero-cost. An empty list is deliberately
    /// NOT "zero-cost only" — that is a complete teardown and gets the plain success message.
    /// </summary>
    public static bool IsZeroCostOnly(IReadOnlyList<string> leftovers) =>
        leftovers.Count > 0 &&
        leftovers.All(a => a.StartsWith(ZeroCostPrefix, StringComparison.Ordinal));
}
