using Jellyfin.Plugin.PosterOverlays.Badges;
using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// Resolution ladder and video range mapping. The VideoRangeType values below are the ones the
/// reference server actually reports, with their measured counts over 2378 movies: SDR 2088,
/// DOVIWithHDR10 112, HDR10 103, DOVIWithHDR10Plus 65, DOVIWithEL 4, DOVI 2, HDR10Plus 2.
/// </summary>
public class TechnicalBadgesTests
{
    private const string Ladder = "4,5,6,8,10,12,16,24,32";

    [Theory]
    // UHD-1, and DCI 4K, and a scope master - which measures 3840x1608, so the height is
    // cropped while the width stays nominal. That is why the width is the input.
    [InlineData(3840, "4K")]
    [InlineData(4096, "4K")]
    [InlineData(7680, "8K")]
    [InlineData(8192, "8K")]      // DCI 8K, 8.53 raw, snaps down rather than to 10
    [InlineData(15360, "16K")]
    [InlineData(23040, "24K")]
    [InlineData(30720, "32K")]
    [InlineData(1920, null)]      // below the minimum, no badge
    [InlineData(1280, null)]
    [InlineData(0, null)]
    public void MapsWidthToRung(int width, string? expected)
    {
        Assert.Equal(expected, TechnicalBadges.Resolution(width, Ladder, 4));
    }

    [Theory]
    [InlineData("SDR", true, new string[0])]
    [InlineData("Unknown", true, new string[0])]
    [InlineData("", true, new string[0])]
    [InlineData(null, true, new string[0])]
    [InlineData("HDR10", true, new[] { "HDR" })]
    [InlineData("HDR10Plus", true, new[] { "HDR+" })]
    [InlineData("HLG", true, new[] { "HLG" })]
    [InlineData("DOVI", true, new[] { "DV" })]
    [InlineData("DOVIWithSDR", true, new[] { "DV" })]
    [InlineData("DOVIWithEL", true, new[] { "DV" })]
    [InlineData("DOVIWithHDR10", true, new[] { "DV HDR" })]
    [InlineData("DOVIWithHDR10", false, new[] { "DV", "HDR" })]
    [InlineData("DOVIWithHDR10Plus", true, new[] { "DV HDR+" })]
    [InlineData("DOVIWithHLG", true, new[] { "DV HLG" })]
    [InlineData("DOVIInvalid", true, new string[0])]
    public void MapsVideoRange(string? value, bool merge, string[] expected)
    {
        Assert.Equal(expected, TechnicalBadges.VideoRange(value, merge));
    }

    /// <summary>
    /// An unknown future member must not crash and must not invent a badge it cannot justify.
    /// The mapping works on substrings for exactly this reason.
    /// </summary>
    [Fact]
    public void HandlesAnUnknownFutureMember()
    {
        Assert.Equal(new[] { "DV HDR" }, TechnicalBadges.VideoRange("DOVIWithHDR10AndSomethingNew", true));
        Assert.Empty(TechnicalBadges.VideoRange("SomethingEntirelyNew", true));
    }
}
