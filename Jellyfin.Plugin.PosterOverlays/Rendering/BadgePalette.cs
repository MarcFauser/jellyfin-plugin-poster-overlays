using System;
using Jellyfin.Plugin.PosterOverlays.Badges;
using Jellyfin.Plugin.PosterOverlays.Configuration;
using SkiaSharp;

namespace Jellyfin.Plugin.PosterOverlays.Rendering;

/// <summary>
/// Colours of a badge pill.
/// </summary>
/// <param name="Fill">Pill background.</param>
/// <param name="Ink">Lettering.</param>
/// <param name="Border">Outline, transparent when the style has none.</param>
internal sealed record BadgePalette(SKColor Fill, SKColor Ink, SKColor Border)
{
    private static readonly SKColor Slate = new(0x11, 0x14, 0x18, 0xE0);
    private static readonly SKColor Faint = new(0xD4, 0xD4, 0xD8, 0x90);
    private static readonly SKColor Amber = new(0xF5, 0xA5, 0x24);
    private static readonly SKColor Purple = new(0x6D, 0x28, 0xD9);
    private static readonly SKColor Blue = new(0x1D, 0x4E, 0xD8);
    private static readonly SKColor Red = new(0xDC, 0x26, 0x26);
    private static readonly SKColor Teal = new(0x2D, 0xD4, 0xBF);
    private static readonly SKColor Graphite = new(0x3F, 0x3F, 0x46, 0xF0);

    /// <summary>
    /// The audio badge. Its own colour rather than the fallback, because the fallback in the
    /// filled style is <see cref="Red"/> - which is reserved for a source-quality warning and
    /// would make "this copy has Atmos" look like "this copy is a camera rip".
    /// </summary>
    private static readonly SKColor Indigo = new(0x43, 0x38, 0xCA);

    /// <summary>
    /// The same hue as <see cref="Indigo"/>, lightened for use as ink on the dark pill. The filled
    /// colour is too dark to read against Slate.
    /// </summary>
    private static readonly SKColor Lilac = new(0xA5, 0xB4, 0xFC);

    /// <summary>
    /// Picks the colours for one badge.
    /// </summary>
    /// <param name="category">The badge category.</param>
    /// <param name="text">The label, needed to tell DV from HDR inside the range category.</param>
    /// <param name="style">The configured style.</param>
    /// <returns>The palette.</returns>
    public static BadgePalette For(BadgeCategory category, string text, BadgeStyle style)
    {
        ArgumentNullException.ThrowIfNull(text);

        bool dolbyVision = text.StartsWith("DV", StringComparison.Ordinal);

        return style switch
        {
            BadgeStyle.FilledAll => category switch
            {
                BadgeCategory.Edition => new BadgePalette(Graphite, SKColors.White, SKColors.Transparent),
                BadgeCategory.Resolution => new BadgePalette(Blue, SKColors.White, SKColors.Transparent),
                BadgeCategory.VideoRange => dolbyVision
                    ? new BadgePalette(Purple, SKColors.White, SKColors.Transparent)
                    : new BadgePalette(Amber, new SKColor(0x1A, 0x12, 0x00), SKColors.Transparent),
                BadgeCategory.Format => new BadgePalette(Teal, SKColors.White, SKColors.Transparent),
                BadgeCategory.Audio => new BadgePalette(Indigo, SKColors.White, SKColors.Transparent),
                _ => new BadgePalette(Red, SKColors.White, SKColors.Transparent),
            },

            BadgeStyle.FilledAccent => category switch
            {
                BadgeCategory.VideoRange when dolbyVision => new BadgePalette(Purple, SKColors.White, Purple),
                BadgeCategory.VideoRange => new BadgePalette(Slate, Amber, Amber.WithAlpha(0xB0)),
                BadgeCategory.Format => new BadgePalette(Slate, Teal, Teal.WithAlpha(0xC0)),
                BadgeCategory.Source => new BadgePalette(Slate, Red, Red.WithAlpha(0xC0)),
                BadgeCategory.Audio => new BadgePalette(Slate, Lilac, Lilac.WithAlpha(0xB0)),
                _ => new BadgePalette(Slate, SKColors.White, Faint),
            },

            _ => category switch
            {
                BadgeCategory.VideoRange => new BadgePalette(Slate, Amber, Amber.WithAlpha(0xB0)),
                BadgeCategory.Format => new BadgePalette(Slate, Teal, Teal.WithAlpha(0xC0)),
                BadgeCategory.Source => new BadgePalette(Slate, Red, Red.WithAlpha(0xC0)),
                BadgeCategory.Audio => new BadgePalette(Slate, Lilac, Lilac.WithAlpha(0xB0)),
                _ => new BadgePalette(Slate, SKColors.White, Faint),
            },
        };
    }
}
