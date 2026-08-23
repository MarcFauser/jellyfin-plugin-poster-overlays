using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.PosterOverlays.Badges;
using Jellyfin.Plugin.PosterOverlays.Configuration;
using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// What a series or a season may claim, worked out from its episodes.
/// </summary>
/// <remarks>
/// The cases are the ones measured on the reference library, because the rule was wrong until it
/// met them: a series whose every episode exists twice - once 1080p, once 4K - is not "mixed", it
/// is entirely available in 4K, and saying otherwise answers a question nobody asked.
/// </remarks>
public class ChildAggregatorTests
{
    private static BadgePreset Preset
    {
        get
        {
            var p = BuiltInPresets.Get(BuiltInPresets.SeriesId)!;
            p.MaxBadges = 6;
            return p;
        }
    }

    private static ChildAggregator.Copy Copy(int season, int episode, params BadgeSpec[] badges) =>
        new(string.Create(CultureInfo.InvariantCulture, $"{season}:{episode}"), badges);

    private static BadgeSpec Res(string text) => new(BadgeCategory.Resolution, text);

    private static BadgeSpec Range(string text) => new(BadgeCategory.VideoRange, text);

    /// <summary>
    /// Lost in Space: 28 episodes, every one of them present as 1080p DV and as 4K DV. The whole
    /// series is available in 4K, so the badge is plain.
    /// </summary>
    [Fact]
    public void EveryEpisodeHavingABetterCopyIsUniform()
    {
        var copies = new List<ChildAggregator.Copy>();
        for (int e = 1; e <= 28; e++)
        {
            copies.Add(Copy(1, e, Range("DV HDR")));
            copies.Add(Copy(1, e, Res("4K"), Range("DV HDR")));
        }

        var result = ChildAggregator.Combine(copies, Preset);

        Assert.Equal(2, result.Count);
        Assert.All(result, b => Assert.Equal(BadgeAvailability.Uniform, b.Availability));
        Assert.Contains(result, b => b.Text == "4K");
        Assert.Contains(result, b => b.Text == "DV HDR");
    }

    /// <summary>
    /// The control for the one above, and the reason the collapse exists at all. The same 56 files
    /// spread over 56 <b>different</b> episodes instead of 28 duplicated ones must come out
    /// partial - if both cases gave the same answer, the collapse would be doing nothing.
    /// </summary>
    [Fact]
    public void TheSameFilesSpreadOverDistinctEpisodesArePartial()
    {
        var copies = new List<ChildAggregator.Copy>();
        for (int e = 1; e <= 28; e++)
        {
            copies.Add(Copy(1, e, Range("DV HDR")));
            copies.Add(Copy(2, e, Res("4K"), Range("DV HDR")));
        }

        var result = ChildAggregator.Combine(copies, Preset);

        Assert.Equal(BadgeAvailability.Partial, result.Single(b => b.Text == "4K").Availability);
        Assert.Equal(BadgeAvailability.Uniform, result.Single(b => b.Text == "DV HDR").Availability);
    }

    /// <summary>
    /// The Last of Us: season one exists only in 1080p SDR, season two also in 4K DV.
    /// </summary>
    [Fact]
    public void AShowWhoseFirstSeasonIsPlainComesOutPartial()
    {
        var copies = new List<ChildAggregator.Copy>();
        for (int e = 1; e <= 9; e++)
        {
            copies.Add(Copy(1, e));
        }

        for (int e = 1; e <= 7; e++)
        {
            copies.Add(Copy(2, e));
            copies.Add(Copy(2, e, Res("4K"), Range("DV HDR")));
        }

        var result = ChildAggregator.Combine(copies, Preset);

        Assert.Equal(2, result.Count);
        Assert.All(result, b => Assert.Equal(BadgeAvailability.Partial, b.Availability));
    }

    /// <summary>
    /// Danger Mouse: full of duplicates and entirely unremarkable. No badge at all - the partial
    /// state must not fire merely because copies exist.
    /// </summary>
    [Fact]
    public void NothingNotableGivesNoBadgeRatherThanAPartialOne()
    {
        var copies = new List<ChildAggregator.Copy>();
        for (int e = 1; e <= 47; e++)
        {
            copies.Add(Copy(1, e));
            copies.Add(Copy(1, e));
        }

        Assert.Empty(ChildAggregator.Combine(copies, Preset));
    }

    /// <summary>
    /// When a show genuinely spans rungs, the higher one is shown and marked as partial: "8K is
    /// available, but not throughout" is the useful statement, and 4K would understate it.
    /// </summary>
    [Fact]
    public void SpanningTwoRungsShowsTheHigherOneAsPartial()
    {
        var copies = new List<ChildAggregator.Copy>
        {
            Copy(1, 1, Res("4K")),
            Copy(1, 2, Res("8K")),
            Copy(1, 3, Res("4K")),
        };

        var badge = Assert.Single(ChildAggregator.Combine(copies, Preset));
        Assert.Equal("8K", badge.Text);
        Assert.Equal(BadgeAvailability.Partial, badge.Availability);
    }

    [Fact]
    public void TheRichestVideoRangeWins()
    {
        var copies = new List<ChildAggregator.Copy>
        {
            Copy(1, 1, Range("HDR")),
            Copy(1, 1, Range("DV HDR")),
        };

        var badge = Assert.Single(ChildAggregator.Combine(copies, Preset));
        Assert.Equal("DV HDR", badge.Text);
        Assert.Equal(BadgeAvailability.Uniform, badge.Availability);
    }

    [Fact]
    public void TheOrderAndTheMaximumAreThePresetsToDecide()
    {
        var preset = Preset;
        preset.MaxBadges = 1;
        preset.BadgeOrder = "VideoRange,Resolution";

        var copies = new List<ChildAggregator.Copy> { Copy(1, 1, Res("4K"), Range("DV HDR")) };

        var badge = Assert.Single(ChildAggregator.Combine(copies, preset));
        Assert.Equal("DV HDR", badge.Text);
    }

    /// <summary>
    /// A series that gains its last missing 4K episode keeps the same label and still has to be
    /// redrawn, because the badge changes colour. So the availability belongs in the key.
    /// </summary>
    [Fact]
    public void CompletingASeriesChangesTheBadgeKey()
    {
        var partial = ChildAggregator.Combine(
            [Copy(1, 1, Res("4K")), Copy(1, 2)],
            Preset);
        var uniform = ChildAggregator.Combine(
            [Copy(1, 1, Res("4K")), Copy(1, 2, Res("4K"))],
            Preset);

        Assert.Equal("4K", partial.Single().Text);
        Assert.Equal("4K", uniform.Single().Text);
        Assert.NotEqual(BadgeBuilder.KeyOf(partial), BadgeBuilder.KeyOf(uniform));
    }

    [Fact]
    public void NoEpisodesGivesNoBadges()
    {
        Assert.Empty(ChildAggregator.Combine([], Preset));
    }
}
