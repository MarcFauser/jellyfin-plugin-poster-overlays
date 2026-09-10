using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// Keeps the test project compiling against the same Jellyfin and SkiaSharp versions as the plugin
/// it tests, per target framework.
/// </summary>
/// <remarks>
/// Two files have to agree and nothing enforces it: raise Jellyfin.Controller in the plugin, forget
/// the test project, and the suite goes on passing against the previous API while the shipped
/// assembly is built against the new one. That failure is invisible - both build, both run, and
/// the tests are green about something that is no longer what users get.
/// <para>
/// The versions are read out of the project files rather than duplicated here, so this test cannot
/// drift the way a hard-coded expectation would.
/// </para>
/// </remarks>
public class ProjectFileTests
{
    private static readonly Regex ConditionPattern =
        new(@"Condition\s*=\s*""'\$\(TargetFramework\)'\s*==\s*'(?<tfm>[^']+)'""", RegexOptions.Compiled);

    private static readonly Regex ReferencePattern =
        new(@"<PackageReference\s+Include=""(?<id>[^""]+)""\s+Version=""(?<version>[^""]+)""", RegexOptions.Compiled);

    [Theory]
    [InlineData("net9.0")]
    [InlineData("net10.0")]
    public void TheTestProjectUsesTheSamePackagesAsThePlugin(string targetFramework)
    {
        var plugin = PinnedVersions(Read("Jellyfin.Plugin.PosterOverlays", "Jellyfin.Plugin.PosterOverlays.csproj"), targetFramework);
        var tests = PinnedVersions(Read("tests", "Jellyfin.Plugin.PosterOverlays.Tests", "Jellyfin.Plugin.PosterOverlays.Tests.csproj"), targetFramework);

        // Positive control: without it an empty plugin dictionary - a changed file layout, a typo
        // in the condition - would make every comparison below vacuously true.
        Assert.Contains("Jellyfin.Controller", plugin.Keys);
        Assert.Contains("SkiaSharp", plugin.Keys);

        foreach (var (id, version) in plugin)
        {
            if (!tests.TryGetValue(id, out string? theirs))
            {
                // The plugin may reference things the tests do not need; only shared ones matter.
                continue;
            }

            Assert.True(
                string.Equals(version, theirs, StringComparison.Ordinal),
                $"{id} for {targetFramework}: plugin uses {version}, tests use {theirs}");
        }
    }

    /// <summary>
    /// Both projects really do build for both lines, so the check above cannot pass by looking at
    /// a target framework neither of them has.
    /// </summary>
    [Fact]
    public void BothProjectsTargetBothLines()
    {
        foreach (string file in new[] { "Jellyfin.Plugin.PosterOverlays.csproj", "Jellyfin.Plugin.PosterOverlays.Tests.csproj" })
        {
            string text = file.Contains("Tests", StringComparison.Ordinal)
                ? Read("tests", "Jellyfin.Plugin.PosterOverlays.Tests", file)
                : Read("Jellyfin.Plugin.PosterOverlays", file);

            var declared = Regex.Match(text, @"<TargetFrameworks>(?<list>[^<]+)</TargetFrameworks>");
            Assert.True(declared.Success, file + " no longer declares TargetFrameworks");
            Assert.Contains("net9.0", declared.Groups["list"].Value, StringComparison.Ordinal);
            Assert.Contains("net10.0", declared.Groups["list"].Value, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The package versions pinned for one target framework, from the conditional item groups.
    /// </summary>
    private static Dictionary<string, string> PinnedVersions(string projectText, string targetFramework)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        // Walk the conditional groups: everything between one condition and the next belongs to it.
        var conditions = ConditionPattern.Matches(projectText).ToList();
        for (int i = 0; i < conditions.Count; i++)
        {
            if (!string.Equals(conditions[i].Groups["tfm"].Value, targetFramework, StringComparison.Ordinal))
            {
                continue;
            }

            int start = conditions[i].Index;
            int end = i + 1 < conditions.Count ? conditions[i + 1].Index : projectText.Length;
            foreach (Match reference in ReferencePattern.Matches(projectText[start..end]))
            {
                found[reference.Groups["id"].Value] = reference.Groups["version"].Value;
            }
        }

        return found;
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("not found above " + AppContext.BaseDirectory + ": " + string.Join('/', parts));
    }
}
