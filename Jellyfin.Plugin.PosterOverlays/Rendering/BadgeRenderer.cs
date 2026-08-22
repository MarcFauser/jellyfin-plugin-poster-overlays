using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Jellyfin.Plugin.PosterOverlays.Badges;
using Jellyfin.Plugin.PosterOverlays.Configuration;
using SkiaSharp;

namespace Jellyfin.Plugin.PosterOverlays.Rendering;

/// <summary>
/// Draws the badge stack onto an image.
/// </summary>
/// <remarks>
/// Every measurement is relative to the image, never an absolute pixel count: the same code has
/// to produce the same look on a 600x900 master and on a 1000x1500 one.
/// <para>
/// The typeface is embedded in this assembly and loaded from the stream. That is not tidiness
/// but a requirement: SkiaSharp resolves a family name through the host's font configuration,
/// and a Jellyfin container may carry no fonts at all. An embedded face needs neither fontconfig
/// nor a system font.
/// </para>
/// <para>
/// SkiaSharp is referenced compile-only. The server carries its own copy - 3.116.1 on Jellyfin
/// 10.11, 3.119.4 on Jellyfin 12 - and shipping a second one would load it into the plugin's own
/// assembly load context with its own native handles. The API used here is the non-obsolete
/// SKFont surface, which is identical in both versions.
/// </para>
/// </remarks>
internal static class BadgeRenderer
{
    private const string FontResource = "Jellyfin.Plugin.PosterOverlays.Resources.Inter-SemiBold.ttf";

    private static readonly object TypefaceLock = new();
    private static SKTypeface? _typeface;

    /// <summary>
    /// Draws the badges onto an encoded image.
    /// </summary>
    /// <param name="original">The encoded original image.</param>
    /// <param name="badges">The badges, already trimmed to the configured maximum.</param>
    /// <param name="config">The settings.</param>
    /// <returns>The encoded badged image, or null when there was nothing to draw.</returns>
    public static byte[]? Draw(byte[] original, IReadOnlyList<BadgeSpec> badges, PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(badges);
        ArgumentNullException.ThrowIfNull(config);

        if (badges.Count == 0)
        {
            return null;
        }

        SKEncodedImageFormat format = DetectFormat(original);
        using var source = SKBitmap.Decode(original);
        if (source is null)
        {
            return null;
        }

        using var target = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(target))
        {
            canvas.DrawBitmap(source, 0, 0);
            DrawStack(canvas, badges, config, source.Width, source.Height);
        }

        using var image = SKImage.FromBitmap(target);
        using var data = image.Encode(format, config.JpegQuality);
        return data.ToArray();
    }

    private static void DrawStack(SKCanvas canvas, IReadOnlyList<BadgeSpec> badges, PluginConfiguration config, int width, int height)
    {
        float pillHeight = (float)(height * config.PillHeightPercent / 100.0);
        float fontSize = (float)(pillHeight * config.FontSizePercentOfPill / 100.0);
        float padding = (float)(pillHeight * config.PaddingPercentOfPill / 100.0);
        float gap = (float)(pillHeight * config.GapPercentOfPill / 100.0);
        float radius = (float)(pillHeight * config.CornerRadiusPercentOfPill / 100.0);
        float border = Math.Max(1f, (float)(pillHeight * config.BorderWidthPercentOfPill / 100.0));
        float marginX = (float)(width * config.HorizontalMarginPercent / 100.0);
        float marginY = (float)(height * config.VerticalMarginPercent / 100.0);

        bool right = config.Corner is BadgeCorner.TopRight or BadgeCorner.BottomRight;
        bool bottom = config.Corner is BadgeCorner.BottomLeft or BadgeCorner.BottomRight;

        float stackHeight = (badges.Count * pillHeight) + ((badges.Count - 1) * gap);
        float y = bottom ? height - marginY - stackHeight : marginY;

        using var font = new SKFont(Typeface(), fontSize) { Edging = SKFontEdging.SubpixelAntialias };
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = border };
        using var text = new SKPaint { IsAntialias = true };

        foreach (var badge in badges)
        {
            var palette = BadgePalette.For(badge.Category, badge.Text, config.Style);
            float textWidth = font.MeasureText(badge.Text, out SKRect ink, text);
            float pillWidth = textWidth + (2 * padding);
            var rect = right
                ? new SKRect(width - marginX - pillWidth, y, width - marginX, y + pillHeight)
                : new SKRect(marginX, y, marginX + pillWidth, y + pillHeight);

            fill.Color = palette.Fill;
            canvas.DrawRoundRect(rect, radius, radius, fill);

            if (palette.Border.Alpha > 0)
            {
                stroke.Color = palette.Border;
                canvas.DrawRoundRect(SKRect.Inflate(rect, -border / 2, -border / 2), radius, radius, stroke);
            }

            // Centre the ink, not the line box. All-caps labels have no descenders, so a
            // baseline derived from the font metrics sits visibly low inside the pill.
            text.Color = palette.Ink;
            canvas.DrawText(badge.Text, rect.MidX - (textWidth / 2f), rect.MidY - ((ink.Top + ink.Bottom) / 2f), font, text);

            y += pillHeight + gap;
        }
    }

    private static SKTypeface Typeface()
    {
        if (_typeface is not null)
        {
            return _typeface;
        }

        lock (TypefaceLock)
        {
            if (_typeface is null)
            {
                using Stream stream = typeof(BadgeRenderer).GetTypeInfo().Assembly.GetManifestResourceStream(FontResource)
                    ?? throw new InvalidOperationException("The embedded typeface " + FontResource + " is missing from the assembly.");
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                _typeface = SKTypeface.FromData(SKData.CreateCopy(memory.ToArray()))
                    ?? throw new InvalidOperationException("The embedded typeface could not be parsed.");
            }
        }

        return _typeface;
    }

    private static SKEncodedImageFormat DetectFormat(byte[] encoded)
    {
        using var codec = SKCodec.Create(new MemoryStream(encoded, writable: false));
        return codec?.EncodedFormat ?? SKEncodedImageFormat.Jpeg;
    }
}
