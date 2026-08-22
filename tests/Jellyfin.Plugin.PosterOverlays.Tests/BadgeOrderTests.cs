using System.Linq;
using Jellyfin.Plugin.PosterOverlays.Badges;
using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// The stacking order, which is also the priority: what falls off when there are more badges
/// than the maximum is decided here.
/// </summary>
public class BadgeOrderTests
{
    private static readonly BadgeSpec[] All =
    {
        new(BadgeCategory.Edition, "EXT"),
        new(BadgeCategory.Resolution, "4K"),
        new(BadgeCategory.VideoRange, "DV HDR"),
        new(BadgeCategory.Source, "TS"),
    };

    [Fact]
    public void FollowsTheConfiguredOrder()
    {
        var sorted = BadgeBuilder.Order(All, "Source,VideoRange,Resolution,Edition");

        Assert.Equal(
            new[] { BadgeCategory.Source, BadgeCategory.VideoRange, BadgeCategory.Resolution, BadgeCategory.Edition },
            sorted.Select(b => b.Category));
    }

    /// <summary>
    /// A category the setting forgets keeps its place at the end. A typo should cost the order,
    /// not the badge.
    /// </summary>
    [Fact]
    public void AppendsWhatTheSettingForgot()
    {
        var sorted = BadgeBuilder.Order(All, "VideoRange");

        Assert.Equal(BadgeCategory.VideoRange, sorted[0].Category);
        Assert.Equal(3, sorted.Skip(1).Count());
        Assert.Contains(sorted, b => b.Category == BadgeCategory.Edition);
    }

    [Fact]
    public void IgnoresNamesItDoesNotKnow()
    {
        var sorted = BadgeBuilder.Order(All, "Nonsense, Resolution ,AlsoNonsense");

        Assert.Equal(BadgeCategory.Resolution, sorted[0].Category);
        Assert.Equal(4, sorted.Count);
    }

    [Fact]
    public void SurvivesAnEmptySetting()
    {
        Assert.Equal(4, BadgeBuilder.Order(All, string.Empty).Count);
        Assert.Equal(4, BadgeBuilder.Order(All, null).Count);
    }

    /// <summary>
    /// Two badges of one category - Dolby Vision and HDR when they are not merged - must keep
    /// their relative order rather than being shuffled.
    /// </summary>
    [Fact]
    public void KeepsTheOrderWithinACategory()
    {
        var pair = new[]
        {
            new BadgeSpec(BadgeCategory.VideoRange, "DV"),
            new BadgeSpec(BadgeCategory.VideoRange, "HDR"),
            new BadgeSpec(BadgeCategory.Edition, "EXT"),
        };

        var sorted = BadgeBuilder.Order(pair, "VideoRange,Edition");

        Assert.Equal("DV", sorted[0].Text);
        Assert.Equal("HDR", sorted[1].Text);
    }
}
