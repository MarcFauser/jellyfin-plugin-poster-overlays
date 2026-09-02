using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// Checks the settings page against itself: every element the script reaches for has to exist.
/// </summary>
/// <remarks>
/// There was no test here at all, which is how the page could refer to a module that does not
/// exist and silently never fill in a single field. A settings page fails quietly - nothing
/// throws, the browser simply returns null and the code moves on - so the failure looks like a
/// page that was never opened rather than one that is broken.
/// <para>
/// This does not execute the script; there is no JavaScript engine in this test project and
/// adding one to check for typos would be a poor trade. It checks the one thing that is checkable
/// from the text and that actually broke: that ids match between the markup and the code.
/// </para>
/// </remarks>
public class ConfigPageTests
{
    /// <summary>
    /// Only selectors that are complete literals. The closing bracket is the point: the page also
    /// builds selectors, as in <c>querySelector('#p_' + key)</c>, and without the bracket this
    /// pattern reads the literal half of those as an id and reports "p_" as missing. A selector
    /// assembled at run time cannot be checked against the markup, so it is not the subject here.
    /// </summary>
    private static readonly Regex SelectorPattern =
        new(@"querySelector\(\s*'#([A-Za-z][\w-]*)'\s*\)", RegexOptions.Compiled);

    private static readonly Regex IdPattern =
        new(@"\bid=""([A-Za-z][\w-]*)""", RegexOptions.Compiled);

    /// <summary>
    /// Every <c>querySelector('#x')</c> in the page refers to an element the page declares.
    /// </summary>
    [Fact]
    public void EverySelectorHasAnElement()
    {
        string html = Page();
        var declared = IdPattern.Matches(html).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        // Created by script rather than declared in markup, so the id is never in the html.
        declared.Add("PosterOverlaysFloating");

        var missing = SelectorPattern.Matches(html)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Where(id => !declared.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "The script reaches for elements that do not exist: " + string.Join(", ", missing));
    }

    /// <summary>
    /// The measurement can fail. Without this, an empty selector list would pass just as happily.
    /// </summary>
    [Fact]
    public void TheCheckCanFail()
    {
        string html = Page();
        var declared = IdPattern.Matches(html).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(declared);
        Assert.DoesNotContain("NoSuchElementAnywhere", declared, StringComparer.Ordinal);

        // And the selector side finds things at all - a pattern that matched nothing would make
        // the test above vacuous, which is the shape a green check most often takes when wrong.
        var used = SelectorPattern.Matches(html).Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(used);
        Assert.Contains("BadgePreview", used, StringComparer.Ordinal);
    }

    /// <summary>
    /// The SVG imitation of the badges is gone and stays gone.
    /// </summary>
    /// <remarks>
    /// It was a second implementation of the drawing rules - it had to learn the centred corners
    /// separately, and it estimated text width as <c>length * fontSize * 0.62</c> where Skia
    /// measures it. The preview now renders through the server. If any of these names come back,
    /// so has the second implementation.
    /// </remarks>
    [Theory]
    [InlineData("previewSvg")]
    [InlineData("previewRows")]
    [InlineData("markerShape")]
    [InlineData("allowedKindsForPreview")]
    public void TheImitationIsGone(string name)
    {
        Assert.DoesNotContain(name, Page(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The preview asks the server, and sends the unsaved settings when it does.
    /// </summary>
    /// <remarks>
    /// Posting the saved configuration instead would look almost right and be useless: the whole
    /// purpose is seeing a change before saving it. <c>collectGlobals</c> and
    /// <c>collectPresetFields</c> are what move the form into the object that gets posted.
    /// </remarks>
    [Fact]
    public void ThePreviewRendersOnTheServer()
    {
        string html = Page();

        Assert.Contains("PosterOverlays/Preview/", html, StringComparison.Ordinal);
        Assert.Contains("collectGlobals();", html, StringComparison.Ordinal);
        Assert.Contains("collectPresetFields();", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every style rule is scoped to the page, so it cannot reach the rest of Jellyfin.
    /// </summary>
    /// <remarks>
    /// The page states this rule in its own first comment and then had eight rules that broke it -
    /// mine. A plugin settings page is injected into the running client, so an unscoped
    /// <c>.po-hit:hover</c> is a rule about every element in Jellyfin that happens to carry that
    /// class. Nothing collides today, which is exactly why nobody would notice.
    /// <para>
    /// There are no exceptions. An earlier version of this test allowed two, on the grounds that
    /// the floating panel hangs off <c>document.body</c> - which was invented and wrong:
    /// <c>buildFloating</c> ends in <c>page().appendChild(box)</c>, and <c>document.body</c> does
    /// not appear in the file at all. <c>position: fixed</c> places an element against the
    /// viewport; it does not move it out of the page.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryStyleRuleIsScopedToThePage()
    {
        var offenders = UnscopedSelectors(Page());

        Assert.True(
            offenders.Count == 0,
            "These rules would apply to the whole client: " + string.Join(" | ", offenders));
    }

    /// <summary>
    /// The scoping check can fail - proven on a planted rule rather than assumed.
    /// </summary>
    [Fact]
    public void TheScopingCheckCanFail()
    {
        string html = Page();

        // Grafted into the style block, exactly as a careless edit would leave it.
        string planted = html.Replace(
            "    </style>",
            "        .po-planted-global { color: red; }\n    </style>",
            StringComparison.Ordinal);

        Assert.NotEqual(html, planted);

        // By predicate, not by equality: what comes back is the whole selector line,
        // ".po-planted-global { color: red; }", and asking for equality with the class name alone
        // looks right and never matches.
        Assert.Contains(UnscopedSelectors(planted), o => o.Contains(".po-planted-global", StringComparison.Ordinal));
        Assert.Empty(UnscopedSelectors(html));
    }

    /// <summary>
    /// The preview image is capped in both directions.
    /// </summary>
    /// <remarks>
    /// It arrives at the stored size of a poster, 1000x1500 and upwards. <c>max-width</c> on its
    /// own was not a limit: it resolves against the container, and the floating panel is
    /// <c>position:fixed</c> with no width of its own, so the container grew with the picture and
    /// the preview covered the screen. Both ceilings have to be there.
    /// </remarks>
    [Fact]
    public void ThePreviewImageCannotGrowWithoutBound()
    {
        string style = StyleBlock(Page());
        int start = style.IndexOf(".po-shot {", StringComparison.Ordinal);
        Assert.True(start >= 0, "the .po-shot rule is gone");

        string rule = style[start..style.IndexOf('}', start)];
        Assert.Contains("max-width", rule, StringComparison.Ordinal);
        Assert.Contains("max-height", rule, StringComparison.Ordinal);

        int panel = style.IndexOf(".po-floating {", StringComparison.Ordinal);
        Assert.True(panel >= 0, "the .po-floating rule is gone");
        Assert.Contains("max-width", style[panel..style.IndexOf('}', panel)], StringComparison.Ordinal);
    }

    /// <summary>
    /// Finds style rules that are not scoped to the page id.
    /// </summary>
    /// <remarks>
    /// Line based rather than a real CSS parse: the selectors in this file are written one per
    /// line, and a parser would be a second thing to get wrong for no gain here.
    /// </remarks>
    /// <param name="html">The page.</param>
    /// <returns>The offending selector lines.</returns>
    private static List<string> UnscopedSelectors(string html)
    {
        return StyleBlock(html)
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith('.') && (l.Contains('{', StringComparison.Ordinal) || l.EndsWith(',')))
            .Where(l => !l.Contains("#posterOverlaysConfigPage", StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// Pulls out the style block, with comments removed so a selector inside one is not counted.
    /// </summary>
    /// <param name="html">The page.</param>
    /// <returns>The stylesheet text.</returns>
    private static string StyleBlock(string html)
    {
        int from = html.IndexOf("<style>", StringComparison.Ordinal);
        int to = html.IndexOf("</style>", StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, "the page has no style block");

        return Regex.Replace(html[(from + 7)..to], @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
    }

    /// <summary>
    /// Reads the page from the source tree.
    /// </summary>
    /// <remarks>
    /// From disk rather than from the built assembly's resources, so a failure points at the file
    /// somebody edits. Walking up to the repository root keeps it working from whatever directory
    /// the test host chooses.
    /// </remarks>
    /// <returns>The page.</returns>
    private static string Page()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "Jellyfin.Plugin.PosterOverlays",
                "Configuration",
                "configPage.html");

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("configPage.html was not found above " + AppContext.BaseDirectory);
    }
}
