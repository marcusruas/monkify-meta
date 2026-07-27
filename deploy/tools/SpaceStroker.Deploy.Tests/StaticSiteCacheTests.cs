using Shouldly;
using Xunit;

namespace SpaceStroker.Deploy.Tests;

public class StaticSiteCacheTests
{
    [Theory]
    [InlineData(StaticSite.Front, "index.html")]
    [InlineData(StaticSite.Front, "verify.html")]
    [InlineData(StaticSite.Front, "embed-example.html")]
    [InlineData(StaticSite.Lp, "index.html")]
    [InlineData(StaticSite.Lp, "404.html")]
    public void ForPath_RootHtml_IsNoCache(StaticSite site, string path) =>
        StaticSiteCache.ForPath(site, path).ShouldBe(StaticSiteCache.HtmlCacheControl);

    // The lp's /cookies page builds to cookies/index.html. A first-path-segment rule would file it
    // as unhashed 1h and re-create the very bug this card fixes, one directory down.
    [Theory]
    [InlineData("cookies/index.html")]
    [InlineData("a/b/c/deep.html")]
    [InlineData("cookies/INDEX.HTML")]
    public void ForPath_NestedHtml_IsNoCache(string path) =>
        StaticSiteCache.ForPath(StaticSite.Lp, path).ShouldBe(StaticSiteCache.HtmlCacheControl);

    [Theory]
    [InlineData(StaticSite.Front, "js/app.4aa9c7d9.js")]
    [InlineData(StaticSite.Front, "css/app.86b425ff.css")]
    [InlineData(StaticSite.Front, "img/bg.b711f613.png")]
    [InlineData(StaticSite.Front, "media/error1.8abf7418.wav")]
    [InlineData(StaticSite.Lp, "_astro/GameModes.BwyNUO_z.js")]
    public void ForPath_HashedPrefixes_AreImmutable(StaticSite site, string path) =>
        StaticSiteCache.ForPath(site, path).ShouldBe(StaticSiteCache.ImmutableCacheControl);

    [Theory]
    [InlineData(StaticSite.Front, "favicon.ico")]
    [InlineData(StaticSite.Lp, "og-image.png")]
    [InlineData(StaticSite.Lp, "robots.txt")]
    [InlineData(StaticSite.Lp, "sitemap-0.xml")]
    [InlineData(StaticSite.Lp, "sitemap-index.xml")]
    [InlineData(StaticSite.Lp, "fonts/vt323-latin.woff2")]
    public void ForPath_UnhashedNonHtml_IsDefault(StaticSite site, string path) =>
        StaticSiteCache.ForPath(site, path).ShouldBe(StaticSiteCache.DefaultCacheControl);

    // An unrecognised directory must never be treated as immutable — under-cache, never over-cache.
    [Fact]
    public void ForPath_UnknownDirectory_IsDefaultNotImmutable() =>
        StaticSiteCache.ForPath(StaticSite.Front, "something-new/file.bin")
            .ShouldBe(StaticSiteCache.DefaultCacheControl);

    // The two sites have different build layouts; the front's prefixes mean nothing on the lp.
    [Fact]
    public void ForPath_FrontPrefixes_DoNotApplyToLp() =>
        StaticSiteCache.ForPath(StaticSite.Lp, "js/app.4aa9c7d9.js")
            .ShouldBe(StaticSiteCache.DefaultCacheControl);

    [Fact]
    public void ForPath_NormalisesWindowsSeparatorsAndLeadingDot()
    {
        StaticSiteCache.ForPath(StaticSite.Front, @"js\app.4aa9c7d9.js")
            .ShouldBe(StaticSiteCache.ImmutableCacheControl);
        StaticSiteCache.ForPath(StaticSite.Front, "./index.html")
            .ShouldBe(StaticSiteCache.HtmlCacheControl);
    }

    [Theory]
    [InlineData("js/app.4aa9c7d9.js.map", true)]
    [InlineData("js/chunk-vendors.eff41647.js.map", true)]
    [InlineData("js/app.4aa9c7d9.js", false)]
    [InlineData("index.html", false)]
    public void IsExcluded_MatchesSourcemapsOnly(string path, bool excluded) =>
        StaticSiteCache.IsExcluded(path).ShouldBe(excluded);

    [Fact]
    public void HashedPrefixes_AreTheBuildToolsOutputDirectories()
    {
        StaticSiteCache.HashedPrefixes(StaticSite.Front).ShouldBe(["js", "css", "img", "media"]);
        StaticSiteCache.HashedPrefixes(StaticSite.Lp).ShouldBe(["_astro"]);
    }

    // --- Plan ---------------------------------------------------------------------

    // Realistic dist listings: the front from Vue CLI, the lp from Astro (incl. the /cookies page).
    private static readonly string[] FrontRootFiles =
        ["embed-example.html", "favicon.ico", "index.html", "verify.html"];
    private static readonly string[] FrontRootDirs = ["css", "img", "js", "media"];
    private static readonly string[] FrontHtml =
        ["embed-example.html", "index.html", "verify.html"];

    private static readonly string[] LpRootFiles =
    [
        "404.html", "favicon-96.png", "favicon.ico", "favicon.png",
        "index.html", "og-image.png", "robots.txt", "sitemap-0.xml", "sitemap-index.xml",
    ];
    private static readonly string[] LpRootDirs = ["_astro", "cookies", "fonts"];
    private static readonly string[] LpHtml = ["404.html", "cookies/index.html", "index.html"];

    private static IReadOnlyList<UploadStep> FrontPlan() =>
        StaticSiteCache.Plan(StaticSite.Front, @"C:\front\dist", "b-front",
            FrontRootFiles, FrontRootDirs, FrontHtml);

    private static IReadOnlyList<UploadStep> LpPlan() =>
        StaticSiteCache.Plan(StaticSite.Lp, @"C:\lp\dist", "b-lp",
            LpRootFiles, LpRootDirs, LpHtml);

    [Fact]
    public void Plan_AssetSteps_HaveNoDeleteFlag() =>
        FrontPlan().Where(s => s.Kind == UploadStepKind.AssetSync)
            .ShouldAllBe(s => !s.Arguments.Contains(" -d "));

    [Fact]
    public void Plan_AssetSteps_CarryTheImmutableHeader() =>
        FrontPlan().Where(s => s.Kind == UploadStepKind.AssetSync)
            .ShouldAllBe(s => s.Arguments.Contains($"Cache-Control:{StaticSiteCache.ImmutableCacheControl}"));

    [Fact]
    public void Plan_RootStep_DeletesAndCarriesNoCache()
    {
        var root = FrontPlan().Single(s => s.Kind == UploadStepKind.RootSync);
        root.Arguments.ShouldContain(" -d ");
        root.Arguments.ShouldContain($"Cache-Control:{StaticSiteCache.HtmlCacheControl}");
    }

    // gsutil rsync honours only ONE -x, so every exclusion must be a single alternation regex.
    [Fact]
    public void Plan_RootStep_HasExactlyOneExclusionFlag() =>
        FrontPlan().Single(s => s.Kind == UploadStepKind.RootSync)
            .Arguments.Split(" -x ").Length.ShouldBe(2);

    // -x excludes from BOTH copy and delete, which is what stops the root pass's -d from reaping
    // the retained hashed assets.
    [Fact]
    public void RootExclusionRegex_MatchesHashedAssetsAndMapsButNotHtml()
    {
        var regex = new System.Text.RegularExpressions.Regex(
            StaticSiteCache.RootExclusionRegex(StaticSite.Front, FrontRootDirs));

        regex.IsMatch("js/app.4aa9c7d9.js").ShouldBeTrue();
        regex.IsMatch("media/error1.8abf7418.wav").ShouldBeTrue();
        regex.IsMatch("js/app.4aa9c7d9.js.map").ShouldBeTrue();
        regex.IsMatch("index.html").ShouldBeFalse();
        regex.IsMatch("favicon.ico").ShouldBeFalse();
    }

    // The manifests are bucket-only, so nothing in dist matches them — without the shield the root
    // pass's -d would delete the records the prune reads.
    [Theory]
    [InlineData(StaticSite.Front)]
    [InlineData(StaticSite.Lp)]
    public void RootExclusionRegex_ShieldsTheBuildManifests(StaticSite site)
    {
        var dirs = site == StaticSite.Front ? FrontRootDirs : LpRootDirs;
        new System.Text.RegularExpressions.Regex(StaticSiteCache.RootExclusionRegex(site, dirs))
            .IsMatch($"{BuildManifest.Prefix}/20260727T120000Z.txt").ShouldBeTrue();
    }

    [Fact]
    public void RootExclusionRegex_ForLp_UsesAstroAndLeavesFontsAlone()
    {
        var regex = new System.Text.RegularExpressions.Regex(
            StaticSiteCache.RootExclusionRegex(StaticSite.Lp, LpRootDirs));

        regex.IsMatch("_astro/GameModes.BwyNUO_z.js").ShouldBeTrue();
        regex.IsMatch("fonts/vt323-latin.woff2").ShouldBeFalse();
        regex.IsMatch("cookies/index.html").ShouldBeFalse();
    }

    // The HTML must never name a chunk that is not uploaded yet.
    [Fact]
    public void Plan_AssetStepsPrecedeRootStep()
    {
        var plan = FrontPlan();
        var lastAsset = plan.Select((s, i) => (s, i)).Last(x => x.s.Kind == UploadStepKind.AssetSync).i;
        var root = plan.Select((s, i) => (s, i)).Single(x => x.s.Kind == UploadStepKind.RootSync).i;
        lastAsset.ShouldBeLessThan(root);
    }

    // Every rsync uploads at a header at least as conservative as the final value, so an intermediate
    // window can only UNDER-cache. The root pass uploads at no-cache even for files whose final tier
    // is 1h; the setmeta passes relax them afterwards, never the reverse.
    [Fact]
    public void Plan_RsyncStepsNeverUploadWeakerThanFinalHeader()
    {
        var plan = FrontPlan();
        plan.Single(s => s.Kind == UploadStepKind.RootSync)
            .Arguments.ShouldContain($"Cache-Control:{StaticSiteCache.HtmlCacheControl}");
        plan.Where(s => s.Kind == UploadStepKind.AssetSync)
            .ShouldAllBe(s => s.Arguments.Contains(StaticSiteCache.ImmutableCacheControl));
    }

    [Fact]
    public void Plan_EmitsASetMetaStepForEveryTier()
    {
        var setMeta = FrontPlan().Where(s => s.Kind == UploadStepKind.SetMeta).ToArray();
        setMeta.ShouldContain(s => s.Arguments.Contains(StaticSiteCache.ImmutableCacheControl));
        setMeta.ShouldContain(s => s.Arguments.Contains($"Cache-Control:{StaticSiteCache.HtmlCacheControl}"));
        setMeta.ShouldContain(s => s.Arguments.Contains(StaticSiteCache.DefaultCacheControl));
    }

    // An empty-target setmeta is an error, so a tier with nothing to stamp gets no step at all.
    [Fact]
    public void Plan_OmitsSetMetaStepWhenATierHasNoTargets()
    {
        var plan = StaticSiteCache.Plan(StaticSite.Front, @"C:\front\dist", "b-front",
            rootFiles: ["index.html"], rootDirectories: ["js"], htmlPaths: ["index.html"]);

        plan.Where(s => s.Kind == UploadStepKind.SetMeta)
            .ShouldAllBe(s => s.Arguments.Contains("gs://"));
        plan.ShouldNotContain(s =>
            s.Kind == UploadStepKind.SetMeta && s.Arguments.Contains(StaticSiteCache.DefaultCacheControl));
    }

    [Fact]
    public void Plan_AssetStepsOnlyForPrefixesActuallyPresent()
    {
        var plan = StaticSiteCache.Plan(StaticSite.Front, @"C:\front\dist", "b-front",
            FrontRootFiles, rootDirectories: ["css", "js"], htmlPaths: FrontHtml);

        var assets = plan.Where(s => s.Kind == UploadStepKind.AssetSync).ToArray();
        assets.Length.ShouldBe(2);
        assets.ShouldAllBe(s => !s.Arguments.Contains("media") && !s.Arguments.Contains("img"));
    }

    [Fact]
    public void Plan_HtmlSetMetaIncludesNestedPages() =>
        LpPlan().Single(s => s.Kind == UploadStepKind.SetMeta
                          && s.Arguments.Contains($"Cache-Control:{StaticSiteCache.HtmlCacheControl}"))
            .Arguments.ShouldContain("gs://b-lp/cookies/index.html");

    // A page directory must NOT get the recursive 1h stamp: that would over-cache its index.html for
    // however long the remaining steps take. Its HTML is covered by the html setmeta step instead.
    [Fact]
    public void Plan_DefaultSetMeta_SkipsPageDirectories()
    {
        var oneHour = LpPlan().Single(s => s.Kind == UploadStepKind.SetMeta
                                        && s.Arguments.Contains(StaticSiteCache.DefaultCacheControl));

        oneHour.Arguments.ShouldContain("gs://b-lp/fonts");
        oneHour.Arguments.ShouldNotContain("gs://b-lp/cookies");
        oneHour.Arguments.ShouldContain("gs://b-lp/robots.txt");
        oneHour.Arguments.ShouldNotContain("gs://b-lp/index.html");
    }

    [Fact]
    public void Plan_LpUsesAstroPrefixOnly()
    {
        var plan = LpPlan();
        plan.Count(s => s.Kind == UploadStepKind.AssetSync).ShouldBe(1);
        plan.ShouldAllBe(s => !s.Arguments.Contains("gs://b-lp/js")
                           && !s.Arguments.Contains("gs://b-lp/media"));
    }

    [Fact]
    public void Plan_EveryRsyncStepExcludesSourcemaps() =>
        FrontPlan().Where(s => s.Kind != UploadStepKind.SetMeta)
            .ShouldAllBe(s => s.Arguments.Contains(".map$"));
}
