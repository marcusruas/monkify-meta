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
}
