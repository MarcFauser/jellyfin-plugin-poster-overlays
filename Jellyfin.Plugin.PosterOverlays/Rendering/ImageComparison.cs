using System;
using SkiaSharp;

namespace Jellyfin.Plugin.PosterOverlays.Rendering;

/// <summary>
/// Decides whether two encoded images show the same picture, allowing for nothing more than the
/// noise a JPEG encoder adds.
/// </summary>
/// <remarks>
/// <b>Needed by the rebuild, and only there.</b> To prove that the cover on an item is one this
/// plugin drew, the rebuild draws the badges onto the cached original again and compares. A
/// byte-identical result is the strong proof, and it is also the usual one: a cover badged under
/// Jellyfin 10.11 was encoded by SkiaSharp 3.116, the redraw on Jellyfin 12 uses 3.119, and on
/// 2026-09-30 the two produced the same bytes for the same poster at 1000 and at 400 pixels.
/// That was measured with the Windows builds of the native library, though, not with the Linux one
/// the server runs - so this comparison is the fallback for the case where the bytes differ and
/// the picture does not.
/// <para>
/// <b>The verdict rests on the WORST block, not on an average.</b> The difference that matters is
/// a badge that is there in one image and not in the other, or says "4K" in one and "8K" in the
/// other - a few hundred pixels out of a million and a half. Averaged over the whole image that
/// vanishes into the encoder noise. So the picture is cut into 16 by 16 blocks, each block gets its
/// mean difference, and the largest block decides. Noise is spread thinly over every block; a
/// badge is concentrated in a few.
/// </para>
/// </remarks>
internal static class ImageComparison
{
    /// <summary>
    /// The block edge length in pixels.
    /// </summary>
    private const int Block = 16;

    /// <summary>
    /// The largest mean difference per block, per colour channel on a 0-255 scale, that still counts
    /// as the same picture.
    /// </summary>
    /// <remarks>
    /// Set from measurement, not chosen - <c>ImageComparisonTests</c> asserts both sides of the
    /// margin on textured posters of 1000, 680 and 400 pixels width. Measured on 2026-09-30:
    /// <list type="bullet">
    /// <item>re-encoding a badged cover once or twice at the same quality: 3.6 to 7.1;</item>
    /// <item>re-encoding it ten quality steps lower, which the plugin never does: up to 10.1;</item>
    /// <item>one letter of one badge different ("4K" against "8K"): 102.7 at 1000 pixels, but only
    /// 41.9 at 400 - the smallest real difference, and the one that sets the ceiling;</item>
    /// <item>a missing badge, no badges, the stack in another corner, another poster: 97 to 141.</item>
    /// </list>
    /// 16 keeps a factor of two to the realistic noise and to the smallest real difference. A first
    /// guess of 12 failed that test on the noise side.
    /// <para>
    /// A false "same" would record the wrong badge set for an item - wrong until the next change,
    /// never stacked, because every redraw starts from the cached original. A false "different"
    /// only puts the item on the rebuild's list. So when in doubt, lower rather than higher.
    /// </para>
    /// </remarks>
    internal const double SamePictureThreshold = 16.0;

    /// <summary>
    /// Says whether two images show the same picture.
    /// </summary>
    /// <param name="a">One encoded image.</param>
    /// <param name="b">The other.</param>
    /// <returns>True when both decode, have the same size and no block differs by more than the threshold.</returns>
    public static bool SamePicture(byte[] a, byte[] b)
    {
        double? worst = WorstBlockDifference(a, b);
        return worst is double value && value <= SamePictureThreshold;
    }

    /// <summary>
    /// Measures how far apart two images are in their most different block.
    /// </summary>
    /// <param name="a">One encoded image.</param>
    /// <param name="b">The other.</param>
    /// <returns>
    /// The mean difference of the worst block, per channel on a 0-255 scale; null when either image
    /// cannot be decoded or the two differ in size - in which case they are not the same picture.
    /// </returns>
    public static double? WorstBlockDifference(byte[] a, byte[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        using var first = Decode(a);
        using var second = Decode(b);
        if (first is null || second is null
            || first.Width != second.Width
            || first.Height != second.Height)
        {
            return null;
        }

        int width = first.Width;
        int height = first.Height;
        ReadOnlySpan<byte> p = first.GetPixelSpan();
        ReadOnlySpan<byte> q = second.GetPixelSpan();
        int stride = first.RowBytes;

        double worst = 0;
        for (int top = 0; top < height; top += Block)
        {
            int bottom = Math.Min(top + Block, height);
            for (int left = 0; left < width; left += Block)
            {
                int right = Math.Min(left + Block, width);
                long sum = 0;
                for (int y = top; y < bottom; y++)
                {
                    int row = y * stride;
                    for (int x = left; x < right; x++)
                    {
                        int i = row + (x * 4);

                        // Red, green, blue. Alpha is left out: both images are decoded to opaque
                        // premultiplied RGBA, and a JPEG has no alpha to compare.
                        sum += Math.Abs(p[i] - q[i]) + Math.Abs(p[i + 1] - q[i + 1]) + Math.Abs(p[i + 2] - q[i + 2]);
                    }
                }

                double mean = sum / (3.0 * (bottom - top) * (right - left));
                if (mean > worst)
                {
                    worst = mean;
                }
            }
        }

        return worst;
    }

    private static SKBitmap? Decode(byte[] encoded)
    {
        // The codec explicitly, for the reason given in BadgeRenderer.Draw: on SkiaSharp 3 the
        // byte[] overload throws on an undecodable image instead of returning null.
        using var data = SKData.CreateCopy(encoded);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            return null;
        }

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        return SKBitmap.Decode(codec, info);
    }
}
