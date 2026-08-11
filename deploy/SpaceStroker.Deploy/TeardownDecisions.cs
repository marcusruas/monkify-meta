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

    /// <summary>The exact command that resumes this teardown, plus the raw-Terraform escape hatch.</summary>
    public static string ResumeHint(string environment, bool purge)
    {
        var purgeFlag = purge ? " --purge" : "";
        return
            "Resume with:\n" +
            $"  cd deploy/tools/SpaceStroker.Deploy && dotnet run -- teardown --env {environment}{purgeFlag}\n" +
            "Escape hatch (raw Terraform, from deploy/terraform/workload):\n" +
            $"  terraform destroy -auto-approve -var-file=\"{environment}.tfvars.json\"";
    }

    /// <summary>
    /// The teardown SUCCEEDED — everything billable is gone — but the zero-cost network resources are
    /// still held by the Google-managed Direct VPC egress address, which releases asynchronously.
    /// </summary>
    public static string ZeroCostWarning(IReadOnlyList<string> leftovers, string environment) =>
        "Workload destroyed — ALL BILLABLE RESOURCES ARE GONE.\n" +
        "\n" +
        $"!! {leftovers.Count} zero-cost resource(s) could not be destroyed yet:\n" +
        Indent(leftovers) +
        "!! They are held by the Google-managed Direct VPC egress address\n" +
        "!! (serverless.googleapis.com/.../addressReservations/serverless-ipv4-*), which releases\n" +
        "!! ASYNCHRONOUSLY after Cloud Run is deleted. `gcloud compute addresses delete` cannot force it.\n" +
        "!! Nothing here is billed. Finish the teardown later:\n" +
        Indent(ResumeHint(environment, purge: false));

    /// <summary>Something that costs money survived every destroy pass — this must fail the run.</summary>
    public static string BillableLeftoverError(IReadOnlyList<string> leftovers, string environment, bool purge) =>
        $"Teardown incomplete after {DestroyAttempts} destroy passes — " +
        $"{leftovers.Count} resource(s) are still in the workload state and still billing:\n" +
        Indent(leftovers) +
        "\n" +
        ResumeHint(environment, purge);

    /// <summary>
    /// `--purge` is about to destroy the state bucket, which is the only record of these resources.
    /// Stop instead: an orphaned resource with no state cannot be destroyed by Terraform at all.
    /// </summary>
    public static string PurgeBlockedMessage(IReadOnlyList<string> leftovers, string environment) =>
        $"--purge STOPPED — {leftovers.Count} resource(s) are still in the workload state:\n" +
        Indent(leftovers) +
        "Destroying the Terraform state bucket now would ORPHAN them (no state, no way to destroy).\n" +
        "The DNS zone, the state bucket and the secrets were NOT touched.\n" +
        "\n" +
        ResumeHint(environment, purge: true);

    private static string Indent(IReadOnlyList<string> addresses) =>
        string.Concat(addresses.Select(a => $"!!   {a}\n"));

    private static string Indent(string block) =>
        string.Concat(block.Split('\n').Select(line => $"!!   {line}\n"));
}
