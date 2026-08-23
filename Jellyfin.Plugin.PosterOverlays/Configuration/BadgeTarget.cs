namespace Jellyfin.Plugin.PosterOverlays.Configuration;

/// <summary>
/// The kinds of library entry this plugin can badge, each configured on its own.
/// </summary>
/// <remarks>
/// Deliberately this plugin's own enum rather than Jellyfin's <c>BaseItemKind</c>: these four are
/// the ones that have a policy attached, and the list is not supposed to grow every time Jellyfin
/// gains an item type.
/// </remarks>
public enum BadgeTarget
{
    /// <summary>A film. One file, so nothing has to be aggregated.</summary>
    Movie,

    /// <summary>
    /// A series. Has no resolution of its own - whatever it shows has to be derived from its
    /// episodes.
    /// </summary>
    Series,

    /// <summary>A season. Same as a series, one level down.</summary>
    Season,

    /// <summary>An episode. One file again, and the level where two copies of one episode differ.</summary>
    Episode,
}
