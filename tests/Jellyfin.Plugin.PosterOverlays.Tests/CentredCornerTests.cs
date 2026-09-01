using System;
using System.Collections.Generic;
using Jellyfin.Plugin.PosterOverlays.Badges;
using Jellyfin.Plugin.PosterOverlays.Configuration;
using Jellyfin.Plugin.PosterOverlays.Rendering;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// The centred corners, measured on the pixels rather than on the arithmetic.
/// </summary>
/// <remarks>
/// They exist for a client that crops an image on the sides. Jellyfin's episode list does that
/// with stills, so a badge near either edge is cut in half however large the horizontal margin
/// is - measured on the real thing: 2 % was cut, 5 % was still cut. Centred, the crop can take as
/// much as it likes off both sides and the badge survives, because it is nowhere near an edge.
/// <para>
/// Asserting the drawn pixels and not the computed rectangle is the point. A rectangle can be
/// centred while the pill is drawn somewhere else entirely; only the image says where the ink
/// landed.
/// </para>
/// </remarks>
public class CentredCornerTests
{
    private static readonly IReadOnlyList<BadgeSpec> One = new[]
    {
        new BadgeSpec(BadgeCategory.Edition, "ALT"),
    };

    private static BadgePreset Preset(BadgeCorner corner, BadgeDirection direction = BadgeDirection.Horizontal) => new()
    {
        Id = Guid.NewGuid(),
        Name = "test",
        Style = BadgeStyle.DarkPill,
        Corner = corner,
        Direction = direction,
        PillHeightPercent = 10,
        FontSizePercentOfPill = 60,
        PaddingPercentOfPill = 35,
        GapPercentOfPill = 25,
        CornerRadiusPercentOfPill = 20,
        BorderWidthPercentOfPill = 3.5,
        HorizontalMarginPercent = 5,
        VerticalMarginPercent = 6,
        MaxBadges = 5,
        BadgeOrder = "Edition,Resolution,VideoRange,Format,Source",
    };

    /// <summary>
    /// A plain white image, so anything dark in it is the badge.
    /// </summary>
    private static byte[] Blank(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        return data.ToArray();
    }

    /// <summary>
    /// The horizontal span of everything dark, as a fraction of the width.
    /// </summary>
    private static (double Left, double Right) InkSpan(byte[] jpeg)
    {
        using var bitmap = SKBitmap.Decode(jpeg);
        int min = int.MaxValue;
        int max = int.MinValue;

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if ((c.Red + c.Green + c.Blue) / 3 < 110)
                {
                    if (x < min) { min = x; }
                    if (x > max) { max = x; }
                }
            }
        }

        Assert.True(min <= max, "no dark pixels at all - the badge was not drawn, so nothing below means anything");
        return (min / (double)bitmap.Width, (max + 1) / (double)bitmap.Width);
    }

    [Theory]
    [InlineData(BadgeCorner.TopCentre)]
    [InlineData(BadgeCorner.BottomCentre)]
    public void ACentredBadgeSitsEquallyFarFromBothEdges(BadgeCorner corner)
    {
        byte[] drawn = BadgeRenderer.Draw(Blank(1920, 1080), One, Preset(corner), 95)!;
        var (left, right) = InkSpan(drawn);

        // Same gap on each side, to within a pixel's worth of rounding.
        Assert.Equal(left, 1.0 - right, precision: 2);

        // And it really is in the middle rather than merely symmetric by accident.
        Assert.InRange((left + right) / 2, 0.48, 0.52);
    }

    /// <summary>
    /// The control: without this corner the badge hugs an edge. If it did not, the assertions
    /// above would hold for every setting and prove nothing.
    /// </summary>
    [Theory]
    [InlineData(BadgeCorner.TopLeft, true)]
    [InlineData(BadgeCorner.TopRight, false)]
    public void TheOtherCornersAreNotCentred(BadgeCorner corner, bool expectLeft)
    {
        var (left, right) = InkSpan(BadgeRenderer.Draw(Blank(1920, 1080), One, Preset(corner), 95)!);

        if (expectLeft)
        {
            Assert.InRange(left, 0.04, 0.07);
            Assert.True(right < 0.3, $"expected the badge near the left edge, ink ended at {right:P0}");
        }
        else
        {
            Assert.InRange(1.0 - right, 0.04, 0.07);
            Assert.True(left > 0.7, $"expected the badge near the right edge, ink started at {left:P0}");
        }
    }

    /// <summary>
    /// Centring must survive a crop, which is the whole reason it exists: cut a fifth off each
    /// side and the badge is still fully inside.
    /// </summary>
    [Fact]
    public void ACentredBadgeSurvivesACropThatWouldCutACornerBadge()
    {
        var centred = InkSpan(BadgeRenderer.Draw(Blank(1920, 1080), One, Preset(BadgeCorner.TopCentre), 95)!);
        var cornered = InkSpan(BadgeRenderer.Draw(Blank(1920, 1080), One, Preset(BadgeCorner.TopLeft), 95)!);

        const double Crop = 0.20;

        Assert.True(centred.Left > Crop && centred.Right < 1 - Crop, "the centred badge should survive a 20 % crop per side");
        Assert.True(cornered.Left < Crop, "the corner badge should NOT survive it - otherwise the comparison says nothing");
    }

    /// <summary>
    /// Stacked pills differ in width, so each is centred on its own. A column centred as a block
    /// would leave the narrow pill visibly off to one side.
    /// </summary>
    [Fact]
    public void StackedPillsAreEachCentredRatherThanTheColumn()
    {
        var badges = new[]
        {
            new BadgeSpec(BadgeCategory.Edition, "REMASTERED"),
            new BadgeSpec(BadgeCategory.Resolution, "4K"),
        };

        byte[] drawn = BadgeRenderer.Draw(
            Blank(1920, 1080), badges, Preset(BadgeCorner.TopCentre, BadgeDirection.Vertical), 95)!;

        using var bitmap = SKBitmap.Decode(drawn);
        // Measure each pill's row band separately: the top one is the wide label, the second the
        // narrow one. Both must straddle the centre line.
        foreach (int y in new[] { (int)(bitmap.Height * 0.10), (int)(bitmap.Height * 0.21) })
        {
            int min = int.MaxValue, max = int.MinValue;
            for (int x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if ((c.Red + c.Green + c.Blue) / 3 < 110)
                {
                    if (x < min) { min = x; }
                    if (x > max) { max = x; }
                }
            }

            Assert.True(min <= max, $"no pill found on row {y}");
            double centre = (min + max) / 2.0 / bitmap.Width;
            Assert.InRange(centre, 0.47, 0.53);
        }
    }
}
