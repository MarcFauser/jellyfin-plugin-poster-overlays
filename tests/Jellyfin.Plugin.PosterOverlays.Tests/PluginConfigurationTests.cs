using Jellyfin.Plugin.PosterOverlays.Badges;
using Jellyfin.Plugin.PosterOverlays.Configuration;
using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// The defaults ship with the plugin, so a slip here is a slip in everybody's installation.
/// </summary>
public class PluginConfigurationTests
{
    [Fact]
    public void DryRunIsOffByDefault()
    {
        // A plugin that reports instead of working, because a debugging switch was left on, is
        // worse than one that does nothing: it looks like it is running.
        Assert.False(new PluginConfiguration().DryRun);
    }

    [Fact]
    public void TheDefaultsMatchWhatWasChosenOnRenderedComparisons()
    {
        var config = new PluginConfiguration();

        Assert.Equal(5.5, config.PillHeightPercent);
        Assert.Equal(60, config.FontSizePercentOfPill);
        Assert.Equal(3, config.MaxBadges);
        Assert.Equal(BadgeStyle.DarkPill, config.Style);
        Assert.Equal(BadgeCorner.TopRight, config.Corner);
        Assert.True(config.MergeDolbyVisionAndHdr);
        Assert.False(config.WriteToMediaFolder);
    }

    /// <summary>
    /// The ladder is a string in the settings, so it can be wrong in a way the compiler cannot
    /// see. This pins that the shipped default actually produces the rungs it promises.
    /// </summary>
    [Fact]
    public void TheDefaultLadderResolvesTheRungsItAdvertises()
    {
        var config = new PluginConfiguration();

        Assert.Equal("4K", TechnicalBadges.Resolution(3840, config.ResolutionLadder, config.MinimumResolutionK));
        Assert.Equal("8K", TechnicalBadges.Resolution(7680, config.ResolutionLadder, config.MinimumResolutionK));
        Assert.Equal("32K", TechnicalBadges.Resolution(30720, config.ResolutionLadder, config.MinimumResolutionK));
        Assert.Null(TechnicalBadges.Resolution(1920, config.ResolutionLadder, config.MinimumResolutionK));
    }
}
