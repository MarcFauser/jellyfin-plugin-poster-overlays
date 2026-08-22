namespace Jellyfin.Plugin.PosterOverlays.Configuration;

/// <summary>
/// Which way the badge stack grows from its corner.
/// </summary>
public enum BadgeDirection
{
    /// <summary>
    /// Downwards, one badge per row. Each pill is only as wide as its own text, so the stack has
    /// a ragged edge but never runs out of room.
    /// </summary>
    Vertical = 0,

    /// <summary>
    /// Along the top or bottom edge, all badges in one row. Compact, and it leaves the rest of
    /// the poster clear - but three or four badges can reach a long way across a narrow image.
    /// </summary>
    Horizontal = 1,
}
