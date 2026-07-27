using Shouldly;
using Xunit;

namespace SpaceStroker.Deploy.Tests;

public class BuildManifestTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NameFor_RoundTripsThroughTimestampOf() =>
        BuildManifest.TimestampOf($"_builds/{BuildManifest.NameFor(Now)}").ShouldBe(Now);

    [Fact]
    public void TimestampOf_ReadsAFullObjectPath() =>
        BuildManifest.TimestampOf("gs://b/_builds/20260727T120000Z.txt").ShouldBe(Now);

    [Theory]
    [InlineData("_builds/not-a-timestamp.txt")]
    [InlineData("_builds/20260727T120000Z.json")]
    [InlineData("")]
    public void TimestampOf_ReturnsNullForAnythingUnparseable(string path) =>
        BuildManifest.TimestampOf(path).ShouldBeNull();

    [Fact]
    public void RetainedManifests_KeepsEverythingInsideTheWindow()
    {
        string[] manifests =
        [
            $"_builds/{BuildManifest.NameFor(Now)}",                            // this build
            $"_builds/{BuildManifest.NameFor(Now.AddHours(-3))}",               // earlier today
            $"_builds/{BuildManifest.NameFor(Now - BuildManifest.Retention)}",  // exactly at the edge
        ];

        BuildManifest.RetainedManifests(manifests, Now).Count.ShouldBe(3);
        BuildManifest.ExpiredManifests(manifests, Now).ShouldBeEmpty();
    }

    [Fact]
    public void RetainedManifests_DropsAnythingPastTheWindow()
    {
        var stale = $"_builds/{BuildManifest.NameFor(Now.AddHours(-25))}";
        string[] manifests = [$"_builds/{BuildManifest.NameFor(Now)}", stale];

        BuildManifest.RetainedManifests(manifests, Now).ShouldNotContain(stale);
        BuildManifest.ExpiredManifests(manifests, Now).ShouldBe([stale]);
    }

    // An unreadable name must never be read as "expired" — we cannot know what it protects.
    [Fact]
    public void RetainedManifests_KeepsUndateableManifests()
    {
        string[] manifests = ["_builds/garbage.txt"];

        BuildManifest.RetainedManifests(manifests, Now).ShouldBe(["_builds/garbage.txt"]);
        BuildManifest.ExpiredManifests(manifests, Now).ShouldBeEmpty();
    }

    [Fact]
    public void PruneTargets_DeletesOnlyWhatNoRetainedBuildNames()
    {
        string[] inBucket = ["js/new.js", "js/old.js", "img/logo.png"];
        string[] keep = ["js/new.js", "img/logo.png"];

        BuildManifest.PruneTargets(inBucket, keep).ShouldBe(["js/old.js"]);
    }

    // Fail closed: an empty keep-set means no manifest could be read, and pruning everything because
    // a `gsutil cat` failed would take the site down.
    [Fact]
    public void PruneTargets_WithNoKeepSet_PrunesNothing() =>
        BuildManifest.PruneTargets(["js/a.js", "js/b.js"], []).ShouldBeEmpty();

    [Fact]
    public void ParseListing_DropsDirectoryHeadersAndBlanks()
    {
        var stdout = "gs://b/js/:\r\n" +
                     "gs://b/js/app.4aa9c7d9.js\n" +
                     "\n" +
                     "  gs://b/js/chunk-vendors.eff41647.js  \n" +
                     "gs://b/img/\n" +
                     "gs://other-bucket/js/nope.js\n";

        BuildManifest.ParseListing(stdout, "b")
            .ShouldBe(["js/app.4aa9c7d9.js", "js/chunk-vendors.eff41647.js"]);
    }

    [Fact]
    public void ParseManifestBodies_UnionsAndDeduplicates()
    {
        // `gsutil cat` of several manifests at once: we want the union, so the concatenation is fine.
        var stdout = "js/app.a.js\nimg/logo.png\n\njs/app.a.js\njs/app.b.js\n";

        BuildManifest.ParseManifestBodies(stdout)
            .ShouldBe(["img/logo.png", "js/app.a.js", "js/app.b.js"]);
    }

    [Fact]
    public void Render_IsOnePathPerLineSorted() =>
        BuildManifest.Render(["js/b.js", "css/a.css"]).ShouldBe("css/a.css\njs/b.js");

    [Fact]
    public void Batch_SplitsOnTheLineLengthCap()
    {
        var targets = Enumerable.Range(0, 10).Select(i => new string('x', 100) + i).ToArray();

        var batches = BuildManifest.Batch(targets, maxLineLength: 300);

        batches.SelectMany(b => b).ShouldBe(targets);
        batches.ShouldAllBe(b => b.Sum(t => t.Length + 1) <= 300 || b.Count == 1);
        batches.Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void Batch_EmptyInput_ProducesNoBatches() =>
        BuildManifest.Batch([]).ShouldBeEmpty();

    // A single target longer than the cap still has to go out — one per line rather than dropped.
    [Fact]
    public void Batch_OversizedSingleTarget_IsItsOwnBatch() =>
        BuildManifest.Batch([new string('x', 500)], maxLineLength: 100).Count.ShouldBe(1);
}
