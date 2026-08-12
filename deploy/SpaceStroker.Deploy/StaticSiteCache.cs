namespace SpaceStroker.Deploy;

/// <summary>Which static site is being published — the two have different build layouts.</summary>
public enum StaticSite { Front, Lp }

/// <summary>What a step does, so tests assert on shape rather than parse argument strings.</summary>
public enum UploadStepKind { AssetSync, RootSync, SetMeta }

/// <summary>One fully-formed gsutil invocation. Deployer executes; StaticSiteCache decides.</summary>
public sealed record UploadStep(UploadStepKind Kind, string Arguments);

/// <summary>
/// The pure decisions behind publishing a static site: which Cache-Control each file class gets and
/// which prefixes are content-hashed (and therefore safe to retain and cache forever). No I/O —
/// <see cref="Deployer"/> keeps every process call, which is what makes this testable at all.
/// </summary>
public static class StaticSiteCache
{
    /// <summary>
    /// Entry points that NAME the current hashed chunks, so they must never be cached. no-cache (not
    /// no-store) still stores + revalidates, so a returning visitor gets a cheap ETag 304; Cloud CDN's
    /// CACHE_ALL_STATIC mode declines to edge-cache it either way, which fixes browser and edge at once.
    /// </summary>
    public const string HtmlCacheControl = "no-cache";

    /// <summary>Content-hashed by construction: a new build produces a new filename.</summary>
    public const string ImmutableCacheControl = "public, max-age=31536000, immutable";

    /// <summary>
    /// Everything unhashed and non-HTML. This is exactly the GCS bucket default we inherit today —
    /// set explicitly so the buckets end up fully declared instead of relying on a provider default.
    /// Fonts live here on purpose: they are NOT content-hashed, so an immutable TTL would strand a
    /// font swap for a year with no clean remedy.
    /// </summary>
    public const string DefaultCacheControl = "public, max-age=3600";

    /// <summary>
    /// Sourcemaps are never uploaded — the buckets are publicly readable, and since a deploy now
    /// RETAINS old builds, uploaded maps would accumulate permanently instead of being overwritten.
    /// A Python regex for gsutil's -x; also used locally via <see cref="IsExcluded"/>.
    /// </summary>
    public const string SourcemapPattern = @".*\.map$";

    /// <summary>
    /// Top-level directories whose contents carry a content hash in the filename. Vue CLI emits
    /// js/css/img/media; Astro emits everything hashed into _astro. A directory NOT listed here is
    /// treated as unhashed — the conservative default for build output nobody enumerated.
    /// </summary>
    public static IReadOnlyList<string> HashedPrefixes(StaticSite site) => site switch
    {
        StaticSite.Front => ["js", "css", "img", "media"],
        StaticSite.Lp => ["_astro"],
        _ => throw new DeployException($"Unknown static site '{site}'."),
    };

    /// <summary>True for a path that must never be uploaded.</summary>
    public static bool IsExcluded(string relativePath) =>
        Normalize(relativePath).EndsWith(".map", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The Cache-Control for one dist-relative path. ORDER MATTERS: the .html test runs first and at
    /// ANY depth, because Astro emits directory-style pages — the lp's /cookies page builds to
    /// cookies/index.html, which a first-segment rule would misfile as unhashed 1h and re-create this
    /// very bug one directory down.
    /// </summary>
    public static string ForPath(StaticSite site, string relativePath)
    {
        var path = Normalize(relativePath);

        if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            return HtmlCacheControl;

        var slash = path.IndexOf('/');
        if (slash > 0 && HashedPrefixes(site).Contains(path[..slash], StringComparer.Ordinal))
            return ImmutableCacheControl;

        return DefaultCacheControl;
    }

    /// <summary>
    /// The single -x regex for the root pass: every hashed prefix actually present, plus sourcemaps.
    /// gsutil rsync honours only ONE -x, so the patterns are composed with alternation rather than
    /// repeated flags. -x excludes from BOTH copy and delete, which is exactly what stops this pass's
    /// -d from reaping the assets we deliberately retain.
    /// </summary>
    public static string RootExclusionRegex(StaticSite site, IReadOnlyList<string> rootDirectories)
    {
        // _builds is bucket-only (the prune manifests) and therefore absent from every dist tree, so
        // without this the root pass's -d would delete the very records the prune depends on.
        var shielded = PresentHashedPrefixes(site, rootDirectories).Append(BuildManifest.Prefix).ToArray();
        return $"^({string.Join('|', shielded)})/.*$|{SourcemapPattern}";
    }

    /// <summary>
    /// The ordered gsutil invocations for one publish. rootFiles/rootDirectories are the entries at
    /// the top of distDir and htmlPaths is every *.html in the tree at ANY depth — all already read
    /// by the caller, so this class stays pure.
    ///
    /// The plan is derived from what the build actually emitted rather than a hardcoded file list:
    /// both build tools have added files nobody enumerated (sitemap-0.xml with the sitemap
    /// integration, cookies/index.html with analytics), and deriving means each new one lands in the
    /// conservative 1h tier instead of being missed.
    /// </summary>
    public static IReadOnlyList<UploadStep> Plan(
        StaticSite site,
        string distDir,
        string bucket,
        IReadOnlyList<string> rootFiles,
        IReadOnlyList<string> rootDirectories,
        IReadOnlyList<string> htmlPaths)
    {
        var steps = new List<UploadStep>();
        var hashed = PresentHashedPrefixes(site, rootDirectories);
        var html = htmlPaths.Select(Normalize).Where(p => p.Length > 0).Order(StringComparer.Ordinal).ToArray();

        // 1. Assets FIRST — the HTML must never name a chunk that is not uploaded yet. No -d, so the
        //    previous build's chunks survive for a session that lazily fetches one mid-deploy (the
        //    verify Web Worker in GameFairPanel.vue is the live case).
        foreach (var prefix in hashed)
        {
            steps.Add(new UploadStep(UploadStepKind.AssetSync,
                $"-h \"Cache-Control:{ImmutableCacheControl}\" -m rsync -r -x \"{SourcemapPattern}\" " +
                $"\"{Path.Combine(distDir, prefix)}\" gs://{bucket}/{prefix}"));
        }

        // 2. Root pass — -d still prunes what the build dropped, but -x shields the asset prefixes.
        //    Uploads at no-cache: the conservative value. Step 3 relaxes what deserves it, never the
        //    reverse, so any window between passes can under-cache but never over-cache.
        steps.Add(new UploadStep(UploadStepKind.RootSync,
            $"-h \"Cache-Control:{HtmlCacheControl}\" -m rsync -r -d " +
            $"-x \"{RootExclusionRegex(site, rootDirectories)}\" \"{distDir}\" gs://{bucket}"));

        // 3. Convergence. rsync compares size/mtime/checksum, NOT metadata, so an object it skipped as
        //    unchanged keeps whatever header it already had — these passes are what repair the ~19 MB
        //    of img/ and media/ already live under the old default.
        if (hashed.Count > 0)
            steps.Add(SetMeta(ImmutableCacheControl, hashed.Select(p => $"gs://{bucket}/{p}"), recursive: true));

        // Unhashed leftovers: root files that are not HTML, plus non-hashed root directories. A
        // directory that CONTAINS html is skipped — recursing 1h over it would over-cache its page
        // until the html step ran. Its html is stamped below; anything else inside keeps the root
        // pass's no-cache, which is safe.
        var htmlDirectories = html
            .Where(p => p.Contains('/'))
            .Select(p => p[..p.IndexOf('/')])
            .ToHashSet(StringComparer.Ordinal);

        var defaultTargets = rootFiles
            .Select(Normalize)
            .Where(f => f.Length > 0 && !IsExcluded(f) && !f.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            .Concat(rootDirectories
                .Select(Normalize)
                .Where(d => d.Length > 0 && !hashed.Contains(d) && !htmlDirectories.Contains(d)))
            .Order(StringComparer.Ordinal)
            .Select(t => $"gs://{bucket}/{t}")
            .ToArray();

        if (defaultTargets.Length > 0)
            steps.Add(SetMeta(DefaultCacheControl, defaultTargets, recursive: true));

        // HTML last, so it wins over anything above no matter how the listing is shaped.
        if (html.Length > 0)
            steps.Add(SetMeta(HtmlCacheControl, html.Select(p => $"gs://{bucket}/{p}"), recursive: false));

        return steps;
    }

    /// <summary>Hashed prefixes this build actually emitted — gsutil fails on a missing source.</summary>
    private static IReadOnlyList<string> PresentHashedPrefixes(StaticSite site, IReadOnlyList<string> rootDirectories)
    {
        var present = rootDirectories.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        return HashedPrefixes(site).Where(present.Contains).ToArray();
    }

    /// <summary>
    /// setmeta takes -h as a SUBCOMMAND flag (rsync takes it top-level, before -m) — an easy and
    /// silent thing to get backwards.
    /// </summary>
    private static UploadStep SetMeta(string cacheControl, IEnumerable<string> targets, bool recursive) =>
        new(UploadStepKind.SetMeta,
            $"-m setmeta {(recursive ? "-r " : "")}-h \"Cache-Control:{cacheControl}\" {string.Join(' ', targets)}");

    /// <summary>Dist-relative paths arrive with OS separators; gsutil and the tier rules want '/'.</summary>
    private static string Normalize(string relativePath)
    {
        var path = (relativePath ?? "").Replace('\\', '/').Trim();
        if (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];
        return path.TrimStart('/');
    }
}
