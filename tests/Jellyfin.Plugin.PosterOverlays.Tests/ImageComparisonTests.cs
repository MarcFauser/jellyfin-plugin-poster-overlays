using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.PosterOverlays.Badges;
using Jellyfin.Plugin.PosterOverlays.Configuration;
using Jellyfin.Plugin.PosterOverlays.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// The comparison the rebuild uses to recognise a cover it drew, and above all its threshold.
/// </summary>
/// <remarks>
/// The threshold is only as good as the margin on both sides of it, so both sides are asserted:
/// everything that is the same picture must stay well below, everything that is a different badge
/// must land well above. A test of one side only would pass with a threshold of zero or of 255.
/// </remarks>
public class ImageComparisonTests
{
    private static readonly IReadOnlyList<BadgeSpec> ThreeBadges = new[]
    {
        new BadgeSpec(BadgeCategory.Edition, "EXT"),
        new BadgeSpec(BadgeCategory.Resolution, "4K"),
        new BadgeSpec(BadgeCategory.VideoRange, "DV HDR"),
    };

    private readonly ITestOutputHelper _output;

    public ImageComparisonTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static BadgePreset MoviePreset => BuiltInPresets.Get(BuiltInPresets.MovieId)!;

    /// <summary>
    /// The measurement behind <see cref="ImageComparison.SamePictureThreshold"/>, kept as a test so
    /// the margin is re-measured whenever the renderer or SkiaSharp changes.
    /// </summary>
    [Theory]
    [InlineData(95, 1000)]
    [InlineData(85, 1000)]
    [InlineData(95, 680)]
    [InlineData(95, 400)]
    [InlineData(85, 400)]
    public void TheThresholdSitsBetweenEncoderNoiseAndABadgeDifference(int quality, int width)
    {
        byte[] original = TestPosters.Textured(seed: 7, quality: quality, width: width);
        byte[] badged = BadgeRenderer.Draw(original, ThreeBadges, MoviePreset, quality)!;

        // What a redraw can really differ by: the same quality setting, one or two generations.
        var noise = new Dictionary<string, double>
        {
            ["re-encoded once"] = Measure(badged, TestPosters.Reencode(badged, quality)),
            ["re-encoded twice"] = Measure(badged, TestPosters.Reencode(TestPosters.Reencode(badged, quality), quality)),
        };

        // Beyond anything the plugin does - it never changes the quality between a draw and a
        // redraw - and asserted only to still count as the same picture, without the margin.
        double stress = Measure(badged, TestPosters.Reencode(badged, quality - 10));

        var otherCorner = new BadgePreset { Corner = BadgeCorner.BottomLeft };
        var sameCorner = new BadgePreset { Corner = BadgeCorner.TopRight };
        var signal = new Dictionary<string, double>
        {
            ["one letter differs (4K/8K)"] = Measure(badged, BadgeRenderer.Draw(original, Swap(ThreeBadges, 1, "8K"), MoviePreset, quality)!),
            ["one badge missing"] = Measure(badged, BadgeRenderer.Draw(original, ThreeBadges.Take(2).ToList(), MoviePreset, quality)!),
            ["no badges at all"] = Measure(badged, original),
            ["stack in another corner"] = Measure(
                BadgeRenderer.Draw(original, ThreeBadges, sameCorner, quality)!,
                BadgeRenderer.Draw(original, ThreeBadges, otherCorner, quality)!),
            ["a different poster"] = Measure(badged, BadgeRenderer.Draw(TestPosters.Textured(seed: 8, quality: quality, width: width), ThreeBadges, MoviePreset, quality)!),
        };

        foreach (var pair in noise.Concat(signal).Append(new KeyValuePair<string, double>("re-encoded 10 lower (stress)", stress)))
        {
            _output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"q{quality} w{width}  {pair.Key,-28} {pair.Value,7:F2}"));
        }

        double worstNoise = noise.Values.Max();
        double weakestSignal = signal.Values.Min();

        // A factor of two on each side, not a bare comparison against the threshold: a margin that
        // is merely positive today is one SkiaSharp update away from being negative.
        Assert.True(
            worstNoise * 2 <= ImageComparison.SamePictureThreshold,
            string.Create(CultureInfo.InvariantCulture, $"encoder noise {worstNoise:F2} is too close to the threshold"));
        Assert.True(
            stress <= ImageComparison.SamePictureThreshold,
            string.Create(CultureInfo.InvariantCulture, $"a re-encode ten steps lower ({stress:F2}) no longer counts as the same picture"));
        Assert.True(
            weakestSignal >= ImageComparison.SamePictureThreshold * 2,
            string.Create(CultureInfo.InvariantCulture, $"a real difference measured only {weakestSignal:F2}"));
    }

    [Fact]
    public void DifferentSizesAreNotTheSamePicture()
    {
        byte[] a = TestPosters.Textured(seed: 3);
        using var bitmap = SkiaSharp.SKBitmap.Decode(a);
        using var smaller = bitmap.Resize(new SkiaSharp.SKImageInfo(500, 750), new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear));
        using var image = SkiaSharp.SKImage.FromBitmap(smaller);
        byte[] b = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 95).ToArray();

        Assert.Null(ImageComparison.WorstBlockDifference(a, b));
        Assert.False(ImageComparison.SamePicture(a, b));
    }

    [Fact]
    public void AnUndecodableImageIsNotTheSamePicture()
    {
        byte[] a = TestPosters.Textured(seed: 3);
        Assert.False(ImageComparison.SamePicture(a, System.Text.Encoding.UTF8.GetBytes("not an image")));
    }

    [Fact]
    public void AnImageIsTheSamePictureAsItself()
    {
        byte[] a = TestPosters.Textured(seed: 3);
        Assert.Equal(0.0, ImageComparison.WorstBlockDifference(a, a));
        Assert.True(ImageComparison.SamePicture(a, a));
    }

    private static double Measure(byte[] a, byte[] b) =>
        ImageComparison.WorstBlockDifference(a, b) ?? double.NaN;

    private static List<BadgeSpec> Swap(IReadOnlyList<BadgeSpec> badges, int index, string text)
    {
        var copy = badges.ToList();
        copy[index] = new BadgeSpec(copy[index].Category, text);
        return copy;
    }
}
