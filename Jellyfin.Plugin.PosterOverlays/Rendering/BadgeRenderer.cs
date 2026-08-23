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
    /// <param name="preset">The look.</param>
    /// <param name="jpegQuality">Encoder quality, used only when the source was a JPEG.</param>
    /// <returns>The encoded badged image, or null when there was nothing to draw.</returns>
    public static byte[]? Draw(byte[] original, IReadOnlyList<BadgeSpec> badges, BadgePreset preset, int jpegQuality)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(badges);
        ArgumentNullException.ThrowIfNull(preset);

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
            DrawStack(canvas, badges, preset, source.Width, source.Height);
        }

        using var image = SKImage.FromBitmap(target);
        using var data = image.Encode(format, jpegQuality);
        return data.ToArray();
    }

    private static void DrawStack(SKCanvas canvas, IReadOnlyList<BadgeSpec> badges, BadgePreset preset, int width, int height)
    {
        float pillHeight = (float)(height * preset.PillHeightPercent / 100.0);
        float fontSize = (float)(pillHeight * preset.FontSizePercentOfPill / 100.0);
        float padding = (float)(pillHeight * preset.PaddingPercentOfPill / 100.0);
        float gap = (float)(pillHeight * preset.GapPercentOfPill / 100.0);
        float radius = (float)(pillHeight * preset.CornerRadiusPercentOfPill / 100.0);
        float border = Math.Max(1f, (float)(pillHeight * preset.BorderWidthPercentOfPill / 100.0));
        float marginX = (float)(width * preset.HorizontalMarginPercent / 100.0);
        float marginY = (float)(height * preset.VerticalMarginPercent / 100.0);

        bool right = preset.Corner is BadgeCorner.TopRight or BadgeCorner.BottomRight;
        bool bottom = preset.Corner is BadgeCorner.BottomLeft or BadgeCorner.BottomRight;
        bool horizontal = preset.Direction == BadgeDirection.Horizontal;

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
            var palette = BadgePalette.For(badge.Category, badge.Text, preset.Style);
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

            // The traffic light, where there is anything to signal. It replaces the border colour
            // rather than the fill: the fill is what guarantees the text stays readable over
            // artwork nobody controls, and that job is not up for negotiation.
            bool signalling = preset.CompletenessColours && badge.Availability != BadgeAvailability.NotApplicable;
            bool partial = signalling && badge.Availability == BadgeAvailability.Partial;
            SKColor signal = partial
                ? ParseColour(preset.PartialColour, new SKColor(0xFF, 0xAA, 0x28))
                : ParseColour(preset.UniformColour, new SKColor(0x3E, 0xD6, 0x82));

            if (signalling && preset.Glow)
            {
                DrawGlow(canvas, rect, radius, signal, (float)(pillHeight * preset.GlowRadiusPercentOfPill / 100.0));
            }

            fill.Color = palette.Fill;
            canvas.DrawRoundRect(rect, radius, radius, fill);

            if (partial && preset.PartialMarker != PartialMarker.None)
            {
                DrawPartialMarker(canvas, rect, radius, palette.Fill, preset.PartialMarker);
            }

            SKColor borderColour = signalling ? signal : palette.Border;
            if (borderColour.Alpha > 0)
            {
                stroke.Color = borderColour;
                canvas.DrawRoundRect(SKRect.Inflate(rect, -border / 2, -border / 2), radius, radius, stroke);
            }

            // Centre the ink, not the line box. All-caps labels have no descenders, so a
            // baseline derived from the font metrics sits visibly low inside the pill.
            text.Color = palette.Ink;
            canvas.DrawText(badge.Text, rect.MidX - (widths[i] / 2f), rect.MidY - ((inks[i].Top + inks[i].Bottom) / 2f), font, text);
        }
    }

    /// <summary>
    /// Reads a <c>#RRGGBB</c> setting, falling back rather than throwing.
    /// </summary>
    /// <remarks>
    /// The value comes out of a configuration file a person can edit, so it is foreign input. An
    /// unreadable colour must not stop a library run - but it must not be invented either, which
    /// is why the fallback is the built-in default and the caller reports it.
    /// </remarks>
    private static SKColor ParseColour(string? value, SKColor fallback) =>
        SKColor.TryParse(value, out SKColor parsed) ? parsed : fallback;

    /// <summary>
    /// Lifts the pill off busy artwork with a soft halo in the signal colour.
    /// </summary>
    private static void DrawGlow(SKCanvas canvas, SKRect rect, float radius, SKColor colour, float blurRadius)
    {
        if (blurRadius <= 0)
        {
            return;
        }

        // Sigma rather than radius: Skia's blur takes a standard deviation, and the visible
        // extent is roughly three of them. Dividing by three makes the setting mean what its
        // name says.
        using var glow = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = colour.WithAlpha(0x77),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius / 3f),
        };

        canvas.DrawRoundRect(rect, radius, radius, glow);
    }

    /// <summary>
    /// Marks a pill whose claim only holds for part of what sits underneath it.
    /// </summary>
    /// <remarks>
    /// Always by <b>adding</b> a lighter tone, never by leaving anything unfilled. A half-empty
    /// pill loses the text over the empty half on a bright poster, which is the one thing the pill
    /// exists to prevent.
    /// </remarks>
    private static void DrawPartialMarker(SKCanvas canvas, SKRect rect, float radius, SKColor fill, PartialMarker marker)
    {
        using var lighter = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = Lighten(fill) };

        int saved = canvas.Save();
        canvas.ClipRoundRect(new SKRoundRect(rect, radius, radius), SKClipOperation.Intersect, antialias: true);

        switch (marker)
        {
            case PartialMarker.Hatch:
                DrawHatch(canvas, rect, lighter);
                break;

            case PartialMarker.Vertical:
                canvas.DrawRect(new SKRect(rect.MidX, rect.Top, rect.Right, rect.Bottom), lighter);
                break;

            case PartialMarker.Diagonal:
                using (var slant = new SKPath())
                {
                    slant.MoveTo(rect.Left + (rect.Width * 0.62f), rect.Top);
                    slant.LineTo(rect.Right, rect.Top);
                    slant.LineTo(rect.Right, rect.Bottom);
                    slant.LineTo(rect.Left + (rect.Width * 0.38f), rect.Bottom);
                    slant.Close();
                    canvas.DrawPath(slant, lighter);
                }

                break;

            case PartialMarker.Wave:
                using (var wave = new SKPath())
                {
                    wave.MoveTo(rect.Right, rect.Top);
                    for (int step = 0; step <= 16; step++)
                    {
                        float t = step / 16f;
                        float wx = rect.Left + (rect.Width * 0.5f)
                                   + (float)(Math.Sin(t * Math.PI * 2) * rect.Width * 0.07);
                        wave.LineTo(wx, rect.Top + (rect.Height * t));
                    }

                    wave.LineTo(rect.Right, rect.Bottom);
                    wave.Close();
                    canvas.DrawPath(wave, lighter);
                }

                break;

            default:
                break;
        }

        canvas.RestoreToCount(saved);
    }

    private static void DrawHatch(SKCanvas canvas, SKRect rect, SKPaint paint)
    {
        float step = rect.Height * 0.34f;
        float thickness = rect.Height * 0.13f;
        using var stripe = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thickness,
            Color = paint.Color,
        };

        for (float x = rect.Left - rect.Height; x < rect.Right + rect.Height; x += step)
        {
            canvas.DrawLine(x, rect.Bottom, x + rect.Height, rect.Top, stripe);
        }
    }

    /// <summary>
    /// Blends a colour towards white, keeping its alpha. Used for the "not everywhere" half.
    /// </summary>
    private static SKColor Lighten(SKColor colour)
    {
        const double Amount = 0.30;
        return new SKColor(
            (byte)(colour.Red + ((255 - colour.Red) * Amount)),
            (byte)(colour.Green + ((255 - colour.Green) * Amount)),
            (byte)(colour.Blue + ((255 - colour.Blue) * Amount)),
            colour.Alpha);
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
