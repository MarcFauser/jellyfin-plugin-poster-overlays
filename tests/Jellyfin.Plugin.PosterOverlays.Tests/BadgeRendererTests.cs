using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.PosterOverlays.Badges;
using Jellyfin.Plugin.PosterOverlays.Configuration;
using Jellyfin.Plugin.PosterOverlays.Rendering;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// The renderer, and above all the embedded typeface.
/// </summary>
public class BadgeRendererTests
{
    private static readonly IReadOnlyList<BadgeSpec> ThreeBadges = new[]
    {
        new BadgeSpec(BadgeCategory.Edition, "EXT"),
        new BadgeSpec(BadgeCategory.Resolution, "4K"),
        new BadgeSpec(BadgeCategory.VideoRange, "DV HDR"),
    };

    /// <summary>
    /// The resource name is a claim about how MSBuild names an embedded file, and a wrong claim
    /// fails at runtime inside a Jellyfin server rather than here. So it is asserted here.
    /// </summary>
    [Fact]
    public void TheTypefaceIsEmbeddedUnderTheExpectedName()
    {
        var assembly = typeof(Plugin).Assembly;
        var names = assembly.GetManifestResourceNames();

        Assert.Contains("Jellyfin.Plugin.PosterOverlays.Resources.Inter-SemiBold.ttf", names);
        Assert.Contains("Jellyfin.Plugin.PosterOverlays.Configuration.configPage.html", names);
    }

    [Fact]
    public void DrawsSomethingIntoTheCorner()
    {
        byte[] plain = SolidPoster(SKColors.DarkSlateGray);

        byte[]? badged = BadgeRenderer.Draw(plain, ThreeBadges, new PluginConfiguration());

        Assert.NotNull(badged);
        using var before = SKBitmap.Decode(plain);
        using var after = SKBitmap.Decode(badged);
        Assert.Equal(before.Width, after.Width);
        Assert.Equal(before.Height, after.Height);

        // The top right corner must have changed, and the opposite corner must not: that pins
        // both "it drew" and "it drew in the right place" in one assertion.
        Assert.True(Differs(before, after, after.Width - 200, 10, 190, 250), "nothing was drawn in the top right corner");
        Assert.False(Differs(before, after, 10, after.Height - 260, 190, 250), "the bottom left corner was touched");
    }

    /// <summary>
    /// Without a real typeface Skia still draws the pill, so a "did anything change" test would
    /// pass with no lettering at all. This counts the distinct colours inside the pill with the
    /// text and again with a blank label, and demands a clear separation - the blank run is the
    /// control that makes the number mean something.
    /// </summary>
    /// <remarks>
    /// Two metrics were thrown away getting here, and both looked fine. Counting distinct
    /// colours on a JPEG counts compression noise: 316 of them in the sampled area with a blank
    /// label against 612 with lettering, so an "over twenty" threshold passed on an empty pill.
    /// Counting distinct colours on a PNG counts the anti-aliased pill edge: 68 against 148.
    /// Counting pixels in the ink colour counts ink.
    /// </remarks>
    [Fact]
    public void ActuallyPutsLetteringInsideThePill()
    {
        byte[] plain = SolidPoster(SKColors.DarkSlateGray, SKEncodedImageFormat.Png);
        var blank = new[] { new BadgeSpec(BadgeCategory.Edition, " ") };

        using var withText = SKBitmap.Decode(BadgeRenderer.Draw(plain, ThreeBadges, new PluginConfiguration())!);
        using var without = SKBitmap.Decode(BadgeRenderer.Draw(plain, blank, new PluginConfiguration())!);

        int inked = NearWhitePixels(withText, withText.Width - 190, 15, 180, 70);
        int empty = NearWhitePixels(without, without.Width - 190, 15, 180, 70);

        // The pill is near black and its border composites to about half grey over it, so a
        // near-white pixel in that area can only be a glyph.
        Assert.True(
            empty < 20,
            "the control is broken: a blank label already produced " + empty.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " near-white pixels, so the measurement is not counting ink");
        Assert.True(
            inked > 200,
            "the pill looks empty: only " + inked.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " near-white pixels - no glyphs were drawn, so the embedded typeface did not load");
    }

    [Fact]
    public void DrawsNothingWithoutBadges()
    {
        Assert.Null(BadgeRenderer.Draw(SolidPoster(SKColors.Black), Array.Empty<BadgeSpec>(), new PluginConfiguration()));
    }

    [Fact]
    public void KeepsThePngFormatOfThePngItWasGiven()
    {
        byte[] png = SolidPoster(SKColors.DarkSlateGray, SKEncodedImageFormat.Png);
        byte[]? badged = BadgeRenderer.Draw(png, ThreeBadges, new PluginConfiguration());

        using var codec = SKCodec.Create(new System.IO.MemoryStream(badged!));
        Assert.Equal(SKEncodedImageFormat.Png, codec.EncodedFormat);
    }

    private static byte[] SolidPoster(SKColor colour, SKEncodedImageFormat format = SKEncodedImageFormat.Jpeg)
    {
        using var bitmap = new SKBitmap(1000, 1500, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(colour);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 95);
        return data.ToArray();
    }

    private static bool Differs(SKBitmap a, SKBitmap b, int x, int y, int width, int height)
    {
        for (int px = x; px < x + width; px++)
        {
            for (int py = y; py < y + height; py++)
            {
                if (a.GetPixel(px, py) != b.GetPixel(px, py))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int NearWhitePixels(SKBitmap bitmap, int x, int y, int width, int height)
    {
        int count = 0;
        for (int px = x; px < x + width; px++)
        {
            for (int py = y; py < y + height; py++)
            {
                var c = bitmap.GetPixel(px, py);
                if (c.Red > 200 && c.Green > 200 && c.Blue > 200)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
