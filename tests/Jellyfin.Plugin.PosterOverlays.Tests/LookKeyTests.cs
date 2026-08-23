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
    private static BadgePreset Preset => BuiltInPresets.Get(BuiltInPresets.MovieId)!;

    [Fact]
    public void TheSameSettingsGiveTheSameKey()
    {
        Assert.Equal(
            OverlayApplier.LookKeyOf(Preset, 95),
            OverlayApplier.LookKeyOf(Preset, 95));
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
        var before = Preset;
        var after = Preset;
        int qualityBefore = 95;
        int qualityAfter = 95;

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
            case "JpegQuality": qualityAfter = 90; break;
            default: Assert.Fail("unhandled property " + property); break;
        }

        Assert.NotEqual(
            OverlayApplier.LookKeyOf(before, qualityBefore),
            OverlayApplier.LookKeyOf(after, qualityAfter));
    }

    /// <summary>
    /// The completeness settings reach the pixels too - but only where the traffic light is on at
    /// all, which is what keeps a movie preset producing the key it produced before presets.
    /// </summary>
    [Theory]
    [InlineData("UniformColour")]
    [InlineData("PartialColour")]
    [InlineData("PartialMarker")]
    [InlineData("Glow")]
    [InlineData("GlowRadiusPercentOfPill")]
    public void TheCompletenessSettingsCountWhenTheTrafficLightIsOn(string property)
    {
        var before = Preset;
        var after = Preset;
        before.CompletenessColours = true;
        after.CompletenessColours = true;
        before.Glow = true;
        after.Glow = true;

        switch (property)
        {
            case "UniformColour": after.UniformColour = "#00FF00"; break;
            case "PartialColour": after.PartialColour = "#FF0000"; break;
            case "PartialMarker": after.PartialMarker = PartialMarker.Hatch; break;
            case "Glow": after.Glow = false; break;
            case "GlowRadiusPercentOfPill": after.GlowRadiusPercentOfPill = 40; break;
            default: Assert.Fail("unhandled property " + property); break;
        }

        Assert.NotEqual(OverlayApplier.LookKeyOf(before, 95), OverlayApplier.LookKeyOf(after, 95));
    }

    /// <summary>
    /// And with the traffic light off they must not, or a movie would be redrawn for a colour it
    /// never shows.
    /// </summary>
    [Fact]
    public void TheCompletenessSettingsAreIgnoredWhenTheTrafficLightIsOff()
    {
        var before = Preset;
        var after = Preset;
        after.UniformColour = "#00FF00";
        after.PartialColour = "#FF0000";
        after.PartialMarker = PartialMarker.Hatch;
        after.GlowRadiusPercentOfPill = 99;

        Assert.False(before.CompletenessColours, "the movie preset is supposed to have the traffic light off");
        Assert.Equal(OverlayApplier.LookKeyOf(before, 95), OverlayApplier.LookKeyOf(after, 95));
    }

    /// <summary>
    /// And the other direction, which is the one that costs real work if it is wrong.
    /// </summary>
    [Fact]
    public void SettingsThatDoNotChangeThePixelsLeaveTheKeyAlone()
    {
        var before = Preset;
        var after = Preset;
        after.Id = System.Guid.NewGuid();
        after.Name = "Something else entirely";

        Assert.Equal(OverlayApplier.LookKeyOf(before, 95), OverlayApplier.LookKeyOf(after, 95));
    }

    /// <summary>
    /// The key is written on one run and compared on the next, so it must not depend on the
    /// culture the server happens to run under - "5,5" and "5.5" are one setting, not two.
    /// </summary>
    [Fact]
    public void TheKeyDoesNotDependOnTheCulture()
    {
        var config = Preset;
        config.PillHeightPercent = 5.5;

        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            // new CultureInfo, not GetCultureInfo: the cached one carries no user overrides, so a
            // check against it is systematically gentler than what production code actually meets.
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            string german = OverlayApplier.LookKeyOf(config, 95);

            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            string invariant = OverlayApplier.LookKeyOf(config, 95);

            Assert.Equal(invariant, german);
            Assert.Contains("5.5", german, System.StringComparison.Ordinal);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
