using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace SpaceStroker.Nightfall.Tests.Wwwroot;

/// <summary>
/// The dashboard's overlays (`#veil`, `#hover`, `#marquee`, `#empty`) are shown and hidden ONLY by
/// toggling the `hidden` attribute from app.js. That attribute hides an element through the
/// user-agent rule `[hidden] { display: none }`, which loses the cascade to ANY author `display`
/// declaration matching the same element — so `.veil { display: flex }` left the confirm dialog
/// permanently on screen, over the whole page, from first paint. CANCEL set `hidden = true` again
/// and changed nothing visible: the app was unusable.
///
/// These assets are plain files with no JS test harness, so this is the guard: app.css must carry a
/// global `[hidden]` rule strong enough that no class rule can out-rank it.
/// </summary>
public class StaticAssetsTests
{
    private static string ReadAsset(string fileName)
    {
        // bin/<cfg>/net8.0 sits four levels below tools/ — the same walk CliOptions uses.
        var toolsRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var path = Path.Combine(toolsRoot, "SpaceStroker.Nightfall", "wwwroot", fileName);

        File.Exists(path).ShouldBeTrue($"Could not find {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void The_hidden_attribute_always_wins_over_a_class_that_declares_display()
    {
        // Comments quote the very rule this asserts, so they are stripped before matching.
        var css = Regex.Replace(ReadAsset("app.css"), @"/\*.*?\*/", "", RegexOptions.Singleline);

        var rule = Regex.Match(css, @"\[hidden\][^{]*\{[^}]*\}");

        rule.Success.ShouldBeTrue("app.css has no global [hidden] rule, so any class that declares "
                                  + "`display` leaves an element visible while app.js believes it is hidden.");
        rule.Value.ShouldMatch(@"display\s*:\s*none\s*!important");
    }

    [Fact]
    public void Every_element_app_js_toggles_starts_hidden_in_the_markup()
    {
        var html = ReadAsset("index.html");

        foreach (var id in new[] { "veil", "hover", "marquee" })
        {
            var tag = Regex.Match(html, $@"<[a-z]+[^>]*\bid=""{id}""[^>]*>");
            tag.Success.ShouldBeTrue($"#{id} is missing from index.html");
            tag.Value.ShouldContain("hidden", Case.Sensitive,
                $"#{id} must start hidden — otherwise it is painted before app.js runs.");
        }
    }
}
