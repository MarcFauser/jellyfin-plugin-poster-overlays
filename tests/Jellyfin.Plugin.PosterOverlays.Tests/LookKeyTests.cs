using System.Globalization;
using System.Threading;
using Jellyfin.Plugin.PosterOverlays;
using Jellyfin.Plugin.PosterOverlays.Configuration;
using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// The key that decides whether an already badged poster has to be drawn again.
/// </summary>
/// <remarks>
/// It has to react to everything that changes the pixels and to nothing else. Too little and a
/// new pill height never reaches the library; too much and every save of the settings page
/// orders a full redraw of a few hundred posters.
/// </remarks>
public class LookKeyTests
{
    [Fact]
    public void TheSameSettingsGiveTheSameKey()
    {
        Assert.Equal(
            OverlayApplier.LookKeyOf(new PluginConfiguration()),
            OverlayApplier.LookKeyOf(new PluginConfiguration()));
    }

    [Theory]
    [InlineData("Style")]
    [InlineData("Corner")]
    [InlineData("Direction")]
    [InlineData("PillHeightPercent")]
    [InlineData("FontSizePercentOfPill")]
    [InlineData("PaddingPercentOfPill")]
    [InlineData("GapPercentOfPill")]
    [InlineData("CornerRadiusPercentOfPill")]
    [InlineData("BorderWidthPercentOfPill")]
    [InlineData("HorizontalMarginPercent")]
    [InlineData("VerticalMarginPercent")]
    [InlineData("JpegQuality")]
    public void EverySettingThatChangesThePixelsChangesTheKey(string property)
    {
        var before = new PluginConfiguration();
        var after = new PluginConfiguration();

        switch (property)
        {
            case "Style": after.Style = BadgeStyle.FilledAll; break;
            case "Corner": after.Corner = BadgeCorner.BottomLeft; break;
            case "Direction": after.Direction = BadgeDirection.Horizontal; break;
            case "PillHeightPercent": after.PillHeightPercent = 6.5; break;
            case "FontSizePercentOfPill": after.FontSizePercentOfPill = 55; break;
            case "PaddingPercentOfPill": after.PaddingPercentOfPill = 30; break;
            case "GapPercentOfPill": after.GapPercentOfPill = 20; break;
            case "CornerRadiusPercentOfPill": after.CornerRadiusPercentOfPill = 50; break;
            case "BorderWidthPercentOfPill": after.BorderWidthPercentOfPill = 5; break;
            case "HorizontalMarginPercent": after.HorizontalMarginPercent = 4; break;
            case "VerticalMarginPercent": after.VerticalMarginPercent = 4; break;
            case "JpegQuality": after.JpegQuality = 90; break;
            default: Assert.Fail("unhandled property " + property); break;
        }

        Assert.NotEqual(OverlayApplier.LookKeyOf(before), OverlayApplier.LookKeyOf(after));
    }

    /// <summary>
    /// And the other direction, which is the one that costs real work if it is wrong.
    /// </summary>
    [Fact]
    public void SettingsThatDoNotChangeThePixelsLeaveTheKeyAlone()
    {
        var before = new PluginConfiguration();
        var after = new PluginConfiguration
        {
            Enabled = false,
            DryRun = true,
            WatchForImageChanges = false,
            WriteToMediaFolder = true,
            ExcludedItemIds = "abc\ndef",
            EditionOverrides = "abc = EXT",
        };

        Assert.Equal(OverlayApplier.LookKeyOf(before), OverlayApplier.LookKeyOf(after));
    }

    /// <summary>
    /// The key is written on one run and compared on the next, so it must not depend on the
    /// culture the server happens to run under - "5,5" and "5.5" are one setting, not two.
    /// </summary>
    [Fact]
    public void TheKeyDoesNotDependOnTheCulture()
    {
        var config = new PluginConfiguration { PillHeightPercent = 5.5 };

        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            string german = OverlayApplier.LookKeyOf(config);

            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            string invariant = OverlayApplier.LookKeyOf(config);

            Assert.Equal(invariant, german);
            Assert.Contains("5.5", german, System.StringComparison.Ordinal);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
