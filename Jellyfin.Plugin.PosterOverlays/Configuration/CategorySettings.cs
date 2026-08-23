using System;

namespace Jellyfin.Plugin.PosterOverlays.Configuration;

/// <summary>
/// A policy: what a given kind of library entry is allowed to show, and which look it uses.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="BadgePreset"/>. Everything here depends on the kind of item and
/// therefore cannot live in a preset, or the preset would stop being reusable.
/// </remarks>
public class CategorySettings
{
    /// <summary>
    /// Gets or sets a value indicating whether this kind of item is badged at all.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the preset that supplies the look.
    /// </summary>
    /// <remarks>
    /// An id pointing at either a built-in or a custom preset. A dangling id - the preset was
    /// deleted, or the configuration came from another server - falls back to the built-in for
    /// this category and says so, rather than quietly drawing with something else.
    /// </remarks>
    public Guid PresetId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether edition badges (EXT, DC, UC, ...) may be drawn.
    /// </summary>
    public bool AllowEdition { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether resolution badges (4K, 8K, ...) may be drawn.
    /// </summary>
    public bool AllowResolution { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether video range badges (DV, HDR, ...) may be drawn.
    /// </summary>
    public bool AllowVideoRange { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether presentation-format badges (3D) may be drawn.
    /// </summary>
    public bool AllowFormat { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether source-quality badges (CAM, TS, ...) may be drawn.
    /// </summary>
    public bool AllowSource { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether an episode is only badged when another episode row
    /// carries the same series, season and episode number.
    /// </summary>
    /// <remarks>
    /// Episodes only. On a poster wall a badge helps you choose; in an episode list you already
    /// know which episode you want, and the only open question is which copy. Measured on the
    /// reference library: 896 rows have a twin, against several thousand that merely have
    /// something notable about them.
    /// </remarks>
    public bool OnlyWhereItDisambiguates { get; set; } = true;
}
