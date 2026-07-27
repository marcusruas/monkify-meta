namespace SpaceStroker.Deploy;

/// <summary>
/// The pure decisions behind pruning retained static-site assets. Deploys deliberately keep the
/// previous builds' hashed assets (see <see cref="StaticSiteCache"/>) so a session still running an
/// older build can fetch a lazily-loaded chunk — but "keep forever" is not a policy, so each deploy
/// records the asset paths it published and prunes assets that no retained build still names.
///
/// Retirement time, NOT object age, is what matters: an image uploaded eight months ago and unchanged
/// since has an ancient creation date, so an age-based rule would delete it the instant it dropped out
/// of a build. GCS records no "retired at", hence the manifests — each one is a dated record of what a
/// build contained, and an asset is prunable once no manifest inside the retention window lists it.
///
/// No I/O — <see cref="Deployer"/> runs every gsutil call and passes the captured output in.
/// </summary>
public static class BuildManifest
{
    /// <summary>
    /// Bucket-only prefix holding the manifests. Never present in a dist tree, so the root rsync's
    /// -d would delete it — <see cref="StaticSiteCache.RootExclusionRegex"/> shields it explicitly.
    /// </summary>
    public const string Prefix = "_builds";

    /// <summary>
    /// How long an asset survives after the build that last named it. The binding constraint is NOT
    /// Cache-Control — HTML is no-cache, so it revalidates on every load and can never name stale
    /// chunks. It is an ALREADY-OPEN tab, whose document is in memory and can lazily fetch a chunk
    /// (the provably-fair verify Worker) at any moment. The app gives up on idle sessions after
    /// 30-60 minutes (useIdleDisconnect), so 24h is ~20x that ceiling and still covers the realistic
    /// worst case: a laptop closed overnight and reopened. Cost is a few MB.
    /// </summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    /// <summary>Sortable, filename-safe, and parseable back out of a gsutil listing.</summary>
    public static string NameFor(DateTimeOffset now) =>
        $"{now.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}.txt";

    /// <summary>
    /// The timestamp encoded in a manifest object path, or null when the name does not parse — an
    /// unparseable manifest is never treated as expired (see <see cref="RetainedManifests"/>), because
    /// guessing "old" on something we cannot read would delete assets it may still list.
    /// </summary>
    public static DateTimeOffset? TimestampOf(string objectPath)
    {
        var name = (objectPath ?? "").Split('/').LastOrDefault() ?? "";
        if (!name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return null;

        return DateTimeOffset.TryParseExact(
            name[..^4], "yyyyMMdd'T'HHmmss'Z'", null,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// Manifests whose builds are still protected: anything inside the retention window, plus every
    /// manifest we could not date. Callers add the manifest they just wrote — it is the current build
    /// and is protected regardless.
    /// </summary>
    public static IReadOnlyList<string> RetainedManifests(
        IReadOnlyList<string> manifestPaths, DateTimeOffset now) =>
        manifestPaths
            .Where(p => TimestampOf(p) is not { } stamp || now - stamp <= Retention)
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>Manifests old enough that nothing needs their contents any more.</summary>
    public static IReadOnlyList<string> ExpiredManifests(
        IReadOnlyList<string> manifestPaths, DateTimeOffset now) =>
        manifestPaths
            .Except(RetainedManifests(manifestPaths, now), StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Asset objects no retained build still names. FAILS CLOSED: an empty keep-set means we could not
    /// read a single manifest, and deleting every asset because a `gsutil cat` failed would take the
    /// site down — so that case prunes nothing at all.
    /// </summary>
    public static IReadOnlyList<string> PruneTargets(
        IReadOnlyList<string> bucketAssetPaths, IReadOnlyList<string> keepPaths)
    {
        if (keepPaths.Count == 0)
            return [];

        var keep = keepPaths.ToHashSet(StringComparer.Ordinal);
        return bucketAssetPaths
            .Where(p => p.Length > 0 && !keep.Contains(p))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Object paths (bucket-relative) out of `gsutil ls -r` stdout. The listing carries directory
    /// header lines ("gs://b/js/:") and blanks; both are dropped.
    /// </summary>
    public static IReadOnlyList<string> ParseListing(string stdout, string bucket)
    {
        var root = $"gs://{bucket}/";
        return (stdout ?? "")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(root, StringComparison.Ordinal))
            .Where(line => !line.EndsWith("/:", StringComparison.Ordinal))
            .Where(line => !line.EndsWith("/", StringComparison.Ordinal))
            .Select(line => line[root.Length..])
            .Where(line => line.Length > 0)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Manifest body: one bucket-relative asset path per line.</summary>
    public static string Render(IReadOnlyList<string> assetPaths) =>
        string.Join('\n', assetPaths.Order(StringComparer.Ordinal));

    /// <summary>The union of every retained manifest's contents — `gsutil cat` of several files at once.</summary>
    public static IReadOnlyList<string> ParseManifestBodies(string stdout) =>
        (stdout ?? "")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Splits targets into command lines that stay under the Windows 8191-character cap (the lp alone
    /// publishes ~80 _astro objects, and a delete list can span several builds' worth).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Batch(IReadOnlyList<string> targets, int maxLineLength = 6000)
    {
        var batches = new List<IReadOnlyList<string>>();
        var current = new List<string>();
        var length = 0;

        foreach (var target in targets)
        {
            if (current.Count > 0 && length + target.Length + 1 > maxLineLength)
            {
                batches.Add(current);
                current = [];
                length = 0;
            }
            current.Add(target);
            length += target.Length + 1;
        }

        if (current.Count > 0)
            batches.Add(current);

        return batches;
    }
}
