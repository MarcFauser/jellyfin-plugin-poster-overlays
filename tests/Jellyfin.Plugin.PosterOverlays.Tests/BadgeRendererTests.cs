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
    /// The built-in movie look: portrait, top right, no completeness colours. The same values the
    /// flat configuration had before presets existed, which is why these tests did not have to
    /// change their expectations.
    /// </summary>
    private static BadgePreset MoviePreset => BuiltInPresets.Get(BuiltInPresets.MovieId)!;

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

        byte[]? badged = BadgeRenderer.Draw(plain, ThreeBadges, MoviePreset, 95);

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

        using var withText = SKBitmap.Decode(BadgeRenderer.Draw(plain, ThreeBadges, MoviePreset, 95)!);
        using var without = SKBitmap.Decode(BadgeRenderer.Draw(plain, blank, MoviePreset, 95)!);

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

    /// <summary>
    /// The glow is drawn with a Skia blur mask filter, and until now that was only a plan on
    /// paper - the mock-ups that sold the idea stacked outlines in a different drawing library
    /// altogether. This renders it for real.
    /// </summary>
    /// <remarks>
    /// Sampled in a band strictly <b>above</b> the pill, which starts at 2 % of 1500 px. A halo
    /// that does not reach outside the pill is not a halo, so that band is where the claim lives.
    /// The control is the same picture without the glow: without it, "these pixels differ" would
    /// only say that two renders are not identical.
    /// </remarks>
    [Fact]
    public void TheGlowReallyReachesOutsideThePill()
    {
        var withGlow = MoviePreset;
        withGlow.CompletenessColours = true;
        withGlow.Glow = true;
        withGlow.GlowRadiusPercentOfPill = 60;

        var without = MoviePreset;
        without.CompletenessColours = true;
        without.Glow = false;

        var badges = new[] { new BadgeSpec(BadgeCategory.Resolution, "4K", BadgeAvailability.Uniform) };
        byte[] plain = SolidPoster(SKColors.Black, SKEncodedImageFormat.Png);

        using var lit = SKBitmap.Decode(BadgeRenderer.Draw(plain, badges, withGlow, 95)!);
        using var flat = SKBitmap.Decode(BadgeRenderer.Draw(plain, badges, without, 95)!);
        using var bare = SKBitmap.Decode(plain);

        // The pill top edge is at 2 % of 1500 = 30 px, so rows 4..24 are outside it.
        Assert.True(
            Differs(flat, lit, lit.Width - 320, 4, 300, 20),
            "no glow outside the pill - the blur mask filter drew nothing");

        // Control: without the glow those same rows are untouched, so the assertion above is
        // about the glow and not about two renders differing for any old reason.
        Assert.False(
            Differs(bare, flat, flat.Width - 320, 4, 300, 20),
            "the control is broken: the area above the pill already differs without any glow");
    }

    /// <summary>
    /// And the marker that says "only part of what is underneath has this".
    /// </summary>
    /// <remarks>
    /// Both colours are set to the same value on purpose, so the border is identical in the two
    /// renders and the only thing left to differ is the marker itself. Otherwise this would pass
    /// on the colour change alone and say nothing about whether the marker was drawn.
    /// </remarks>
    [Fact]
    public void ThePartialMarkerIsDrawnInsideThePill()
    {
        var preset = MoviePreset;
        preset.CompletenessColours = true;
        preset.Glow = false;
        preset.PartialMarker = PartialMarker.Diagonal;
        preset.UniformColour = "#3ED682";
        preset.PartialColour = "#3ED682";

        byte[] plain = SolidPoster(SKColors.Black, SKEncodedImageFormat.Png);
        using var uniform = SKBitmap.Decode(
            BadgeRenderer.Draw(plain, [new BadgeSpec(BadgeCategory.Resolution, "4K", BadgeAvailability.Uniform)], preset, 95)!);
        using var partial = SKBitmap.Decode(
            BadgeRenderer.Draw(plain, [new BadgeSpec(BadgeCategory.Resolution, "4K", BadgeAvailability.Partial)], preset, 95)!);

        Assert.True(
            Differs(uniform, partial, partial.Width - 200, 32, 165, 75),
            "the partial marker drew nothing inside the pill");

        // And with the marker switched off the two must be identical, or the difference above
        // could be coming from something other than the marker.
        var none = MoviePreset;
        none.CompletenessColours = true;
        none.Glow = false;
        none.PartialMarker = PartialMarker.None;
        none.UniformColour = "#3ED682";
        none.PartialColour = "#3ED682";

        using var u2 = SKBitmap.Decode(
            BadgeRenderer.Draw(plain, [new BadgeSpec(BadgeCategory.Resolution, "4K", BadgeAvailability.Uniform)], none, 95)!);
        using var p2 = SKBitmap.Decode(
            BadgeRenderer.Draw(plain, [new BadgeSpec(BadgeCategory.Resolution, "4K", BadgeAvailability.Partial)], none, 95)!);

        Assert.False(
            Differs(u2, p2, p2.Width - 200, 32, 165, 75),
            "the control is broken: with the marker off the two renders still differ");
    }

    [Fact]
    public void DrawsNothingWithoutBadges()
    {
        Assert.Null(BadgeRenderer.Draw(SolidPoster(SKColors.Black), Array.Empty<BadgeSpec>(), MoviePreset, 95));
    }

    [Fact]
    public void KeepsThePngFormatOfThePngItWasGiven()
    {
        byte[] png = SolidPoster(SKColors.DarkSlateGray, SKEncodedImageFormat.Png);
        byte[]? badged = BadgeRenderer.Draw(png, ThreeBadges, MoviePreset, 95);

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
