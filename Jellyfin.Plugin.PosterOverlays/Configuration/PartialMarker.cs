namespace Jellyfin.Plugin.PosterOverlays.Configuration;

/// <summary>
/// How a badge is marked when the thing it describes is only partly available.
/// </summary>
/// <remarks>
/// Only series and seasons can be in that state; a film and an episode are one file each. Every
/// option here was drawn onto real posters at real tile size before it was offered, because
/// reasoning about it was repeatedly wrong.
/// <para>
/// What is not on this list, and why: leaving half the pill <b>empty</b>. On a bright poster the
/// text over the empty half disappears, and guaranteeing contrast over artwork nobody controls is
/// the entire reason the pill exists. Every marker below therefore keeps a background under the
/// whole label - the second half is filled in a lighter tone, not left out.
/// </para>
/// </remarks>
public enum PartialMarker
{
    /// <summary>Nothing. The colour alone carries the state, if colours are on at all.</summary>
    None,

    /// <summary>Left half in the normal fill, right half lighter, split straight down.</summary>
    Vertical,

    /// <summary>The same, split on a slant. Reads as deliberate rather than accidental.</summary>
    Diagonal,

    /// <summary>
    /// A wavy boundary instead of a straight one. Measured: at a 16 px pill the wave is about two
    /// pixels and cannot be told from <see cref="Vertical"/>. It does show on a detail page where
    /// the poster is large, and it costs no more to draw, which is why it is offered at all.
    /// </summary>
    Wave,

    /// <summary>Diagonal hatching across the whole pill. Says "different" without saying "half".</summary>
    Hatch,
}
