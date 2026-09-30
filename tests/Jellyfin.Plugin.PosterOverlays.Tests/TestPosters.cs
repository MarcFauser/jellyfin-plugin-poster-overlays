using System;
using SkiaSharp;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// Posters for tests that compare pictures rather than bytes.
/// </summary>
/// <remarks>
/// Textured on purpose. A solid colour is the kindest case a JPEG encoder ever meets - it adds
/// almost no noise - so a comparison calibrated on one would be calibrated on the wrong thing. This
/// one has gradients, hard edges and grain, which is closer to a real cover and gives the encoder
/// something to be wrong about.
/// </remarks>
internal static class TestPosters
{
    /// <summary>
    /// A 1000 by 1500 poster with gradients, shapes and grain.
    /// </summary>
    /// <param name="seed">Varies the picture; the same seed gives the same bytes.</param>
    /// <param name="quality">The JPEG quality.</param>
    /// <param name="width">The width; the height is one and a half times that, as on a poster.</param>
    /// <returns>The encoded JPEG.</returns>
    public static byte[] Textured(int seed = 1, int quality = 95, int width = 1000)
    {
        int height = width * 3 / 2;
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var gradient = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(width, height),
                new[] { new SKColor(20, 40, (byte)(60 + (seed * 17 % 100))), new SKColor(200, 120, 40), new SKColor(30, 30, 30) },
                SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = gradient };
            canvas.DrawRect(0, 0, width, height, paint);

            var random = new Random(seed);
            using var shape = new SKPaint { IsAntialias = true };
            for (int i = 0; i < 60; i++)
            {
                shape.Color = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 160);
                canvas.DrawCircle(random.Next(width), random.Next(height), random.Next(width / 50, width * 16 / 100), shape);
            }
        }

        // Grain, so every block carries detail the encoder has to approximate.
        var grain = new Random(seed + 1000);
        for (int y = 0; y < bitmap.Height; y += 2)
        {
            for (int x = 0; x < bitmap.Width; x += 2)
            {
                var c = bitmap.GetPixel(x, y);
                int d = grain.Next(-18, 19);
                bitmap.SetPixel(x, y, new SKColor(Clamp(c.Red + d), Clamp(c.Green + d), Clamp(c.Blue + d)));
            }
        }

        return Encode(bitmap, quality);
    }

    /// <summary>
    /// Decodes and encodes again - what a different encoder, or a second generation, does to a
    /// picture that has not changed.
    /// </summary>
    /// <param name="jpeg">The encoded image.</param>
    /// <param name="quality">The JPEG quality of the new encoding.</param>
    /// <returns>The re-encoded JPEG.</returns>
    public static byte[] Reencode(byte[] jpeg, int quality)
    {
        using var bitmap = SKBitmap.Decode(jpeg);
        return Encode(bitmap, quality);
    }

    private static byte[] Encode(SKBitmap bitmap, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
}
