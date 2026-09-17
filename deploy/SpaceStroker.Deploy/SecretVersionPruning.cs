namespace SpaceStroker.Deploy;

/// <summary>
/// Decides which Secret Manager versions the tool may destroy, and whether a value is worth pushing
/// at all.
///
/// ⚠️ WHY THIS EXISTS — Secret Manager does not bill per secret, it bills the "Secret version replica
/// storage" SKU per ENABLED VERSION per month (R$0.352219615, first 6 free). <see cref="Deployer.SyncSecrets"/>
/// used to push a new version for EVERY key on EVERY deploy and destroy nothing, so 24 secrets had
/// accumulated 692 live versions by 2026-09 — R$118.77 in the month's first 17 days, on a run rate of
/// ~R$242/month that rose with each deploy and never came down. Almost all of those versions were
/// byte-identical duplicates of each other, and none of them were readable by anything: Cloud Run
/// mounts every secret with <c>version = "latest"</c> (terraform/workload/modules/cloud_run_api/main.tf),
/// which even a rolled-back revision re-resolves, so only the newest enabled version is ever served.
///
/// Access operations are NOT the lever — the project made 1,776 API calls in 30 days against a
/// 10,000/month free tier. Versions are the entire bill.
///
/// Every decision here is irreversible in one direction: a destroyed version's payload cannot be
/// recovered, by anyone, ever. Hence the deliberate strictness — unparseable input throws instead of
/// being skipped, and <see cref="ToDestroy"/> refuses a retention of zero.
/// </summary>
public static class SecretVersionPruning
{
    /// <summary>
    /// The live value plus one rollback step. Chosen over keep-1 so a bad deploy can be undone from
    /// the cloud (`versions access` the previous one, push it back) without depending on
    /// deploy/secrets.&lt;env&gt;.json — which is git-ignored and exists on one workstation.
    /// 24 secrets × 2 ≈ R$14.79/month against ~R$242 before.
    /// </summary>
    public const int VersionsToKeep = 2;

    /// <summary>One row of `gcloud secrets versions list --format="csv[no-heading](name,state)"`.</summary>
    public sealed record SecretVersion(int Number, string State)
    {
        public bool IsEnabled => State.Equals("enabled", StringComparison.OrdinalIgnoreCase);
        public bool IsDestroyed => State.Equals("destroyed", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses gcloud's CSV rows. \r is stripped per line rather than at the call sites: the tool runs
    /// on Windows, where every captured line arrives CRLF-terminated and a trailing \r would break the
    /// version-number parse on all of them.
    /// </summary>
    public static IReadOnlyList<SecretVersion> Parse(string gcloudCsv)
    {
        var versions = new List<SecretVersion>();

        foreach (var raw in (gcloudCsv ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            var parts = line.Split(',');

            // An unrecognised row means gcloud's output shape changed. Skipping it would silently
            // miscount the retention window and destroy a version that should have been kept, so this
            // fails the run instead.
            if (parts.Length < 2 || !int.TryParse(parts[0].Trim(), out var number))
                throw new DeployException(
                    $"Cannot read Secret Manager version list: expected '<number>,<state>' rows, got '{line}'. " +
                    $"Check `gcloud secrets versions list` output and update {nameof(SecretVersionPruning)}.");

            versions.Add(new SecretVersion(number, parts[1].Trim()));
        }

        return versions;
    }

    /// <summary>
    /// The version numbers to destroy, OLDEST FIRST, keeping the newest <paramref name="keep"/>
    /// ENABLED versions.
    ///
    /// Two details carry the safety of this whole feature:
    /// • Ordering is numeric. gcloud prints version names as strings and a text sort puts "9" above
    ///   "10" — that sort would destroy the live value on any secret past version 9.
    /// • Oldest first, so an interruption (Ctrl-C, expired credentials, an API 500) stops short of the
    ///   retention window instead of inside it.
    ///
    /// A DISABLED version never gets a retention slot: it still bills (only a destroyed version stops
    /// the meter) while `latest` skips over it, so it can never be the value Cloud Run mounts.
    /// Already-destroyed versions are left alone — destroying one twice is an error from gcloud.
    /// </summary>
    public static IReadOnlyList<int> ToDestroy(IEnumerable<SecretVersion> versions, int keep = VersionsToKeep)
    {
        if (keep < 1)
            throw new DeployException(
                $"Refusing to prune with keep={keep}: it would destroy the version every running " +
                "container resolves 'latest' to. Retention must be at least 1.");

        var all = versions.ToList();

        var retained = all
            .Where(v => v.IsEnabled)
            .OrderByDescending(v => v.Number)
            .Take(keep)
            .Select(v => v.Number)
            .ToHashSet();

        return all
            .Where(v => !v.IsDestroyed && !retained.Contains(v.Number))
            .Select(v => v.Number)
            .OrderBy(n => n)
            .ToList();
    }

    /// <summary>
    /// Whether Secret Manager already holds exactly this value, so the deploy can skip the
    /// `versions add` that would otherwise bill forever for a duplicate.
    ///
    /// <paramref name="storedPayload"/> is the raw payload bytes gcloud writes with
    /// `versions access --out-file`, compared against the same UTF-8 encoding that
    /// `versions add --data-file` uploads. Bytes rather than text on purpose: the value never crosses
    /// a console, so nothing can re-encode it or rewrite its line endings on the way back, and the
    /// round trip is symmetric with how it was written. (`--format="value(payload.data)"` was the
    /// other candidate and is wrong for this — gcloud decodes it to plaintext and prints it, which
    /// puts private keys on stdout.)
    ///
    /// Comparison is exact. A wrong "changed" only costs one version (the old behaviour); a wrong
    /// "unchanged" would silently leave production on a stale value, so every difference — including
    /// CRLF-vs-LF inside a PEM, which breaks key parsers — counts as a change.
    /// </summary>
    public static bool IsAlreadyStored(string value, byte[]? storedPayload)
    {
        if (storedPayload is null || storedPayload.Length == 0)
            return false;

        return storedPayload.AsSpan().SequenceEqual(System.Text.Encoding.UTF8.GetBytes(value));
    }
}
