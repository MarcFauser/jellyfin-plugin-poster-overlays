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
        bool horizontal = config.Direction == BadgeDirection.Horizontal;

        using var font = new SKFont(Typeface(), fontSize) { Edging = SKFontEdging.SubpixelAntialias };
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = border };
        using var text = new SKPaint { IsAntialias = true };

        // Measure first, place second. A horizontal row anchored on the right has to know how
        // wide the whole row is before the first pill can be positioned.
        var widths = new float[badges.Count];
        var inks = new SKRect[badges.Count];
        float rowWidth = 0;
        for (int i = 0; i < badges.Count; i++)
        {
            widths[i] = font.MeasureText(badges[i].Text, out inks[i], text);
            rowWidth += widths[i] + (2 * padding);
        }

        rowWidth += Math.Max(0, badges.Count - 1) * gap;

        float stackHeight = horizontal
            ? pillHeight
            : (badges.Count * pillHeight) + ((badges.Count - 1) * gap);

        float y = bottom ? height - marginY - stackHeight : marginY;
        float x = right ? width - marginX - rowWidth : marginX;

        for (int i = 0; i < badges.Count; i++)
        {
            var badge = badges[i];
            var palette = BadgePalette.For(badge.Category, badge.Text, config.Style);
            float pillWidth = widths[i] + (2 * padding);

            SKRect rect;
            if (horizontal)
            {
                rect = new SKRect(x, y, x + pillWidth, y + pillHeight);
                x += pillWidth + gap;
            }
            else
            {
                rect = right
                    ? new SKRect(width - marginX - pillWidth, y, width - marginX, y + pillHeight)
                    : new SKRect(marginX, y, marginX + pillWidth, y + pillHeight);
                y += pillHeight + gap;
            }

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
            canvas.DrawText(badge.Text, rect.MidX - (widths[i] / 2f), rect.MidY - ((inks[i].Top + inks[i].Bottom) / 2f), font, text);
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
