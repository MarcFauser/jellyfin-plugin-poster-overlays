namespace Jellyfin.Plugin.PosterOverlays.Badges;

/// <summary>
/// What kind of statement an edition token makes about a release.
/// </summary>
/// <remarks>
/// A folder may carry one badge per facet, because two tokens from different facets say two
/// independent things while two from the same facet say one thing twice. "Extended Remastered"
/// is a cut and a mastering; "Extended Directors Cut" is a cut described twice, and the
/// combination rules in <see cref="EditionCatalog"/> already collapse those into one badge.
/// <para>
/// The declaration order is the drawing order, so the cut comes first: it is what tells two
/// copies of a film apart, while the rest describes how the copy was made.
/// </para>
/// </remarks>
internal enum EditionFacet
{
    /// <summary>
    /// Which cut this is - extended, uncut, theatrical, a named recut, and the packaging labels
    /// that in practice mean a different cut.
    /// </summary>
    Cut,

    /// <summary>
    /// How the picture is framed or coloured rather than what is in it: open matte, IMAX,
    /// black and chrome, colourised.
    /// </summary>
    Presentation,

    /// <summary>
    /// How the copy was mastered rather than what it contains: remastered, restored.
    /// </summary>
    Master,
}
