namespace Jellyfin.Plugin.PosterOverlays.Configuration;

/// <summary>
/// Visual style of the badge pills.
/// </summary>
public enum BadgeStyle
{
    /// <summary>
    /// A dark, slightly translucent pill for every badge, with coloured lettering and a thin
    /// coloured border. Dolby Vision and HDR are merged into a single pill, which keeps the
    /// stack one row shorter. This is the default.
    /// </summary>
    DarkPill = 0,

    /// <summary>
    /// Like <see cref="DarkPill"/>, but Dolby Vision gets its own filled pill and HDR stays a
    /// separate row. Closer to the look most streaming interfaces use, at the cost of a row.
    /// </summary>
    FilledAccent = 1,

    /// <summary>
    /// Every badge is a fully filled pill in its own colour with fully rounded ends.
    /// </summary>
    FilledAll = 2,
}
