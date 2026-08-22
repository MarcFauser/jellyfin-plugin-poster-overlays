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
    private static readonly SKColor Graphite = new(0x3F, 0x3F, 0x46, 0xF0);

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
                _ => new BadgePalette(Red, SKColors.White, SKColors.Transparent),
            },

            BadgeStyle.FilledAccent => category switch
            {
                BadgeCategory.VideoRange when dolbyVision => new BadgePalette(Purple, SKColors.White, Purple),
                BadgeCategory.VideoRange => new BadgePalette(Slate, Amber, Amber.WithAlpha(0xB0)),
                BadgeCategory.Source => new BadgePalette(Slate, Red, Red.WithAlpha(0xC0)),
                _ => new BadgePalette(Slate, SKColors.White, Faint),
            },

            _ => category switch
            {
                BadgeCategory.VideoRange => new BadgePalette(Slate, Amber, Amber.WithAlpha(0xB0)),
                BadgeCategory.Source => new BadgePalette(Slate, Red, Red.WithAlpha(0xC0)),
                _ => new BadgePalette(Slate, SKColors.White, Faint),
            },
        };
    }
}
