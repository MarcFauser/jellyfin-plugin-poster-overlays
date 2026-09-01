namespace Jellyfin.Plugin.PosterOverlays.Configuration;

/// <summary>
/// Corner of the image the badge stack starts in. The stack always grows downwards.
/// </summary>
public enum BadgeCorner
{
    /// <summary>Top right. The default, and where the eye lands first on a card.</summary>
    TopRight = 0,

    /// <summary>Top left.</summary>
    TopLeft = 1,

    /// <summary>Bottom right.</summary>
    BottomRight = 2,

    /// <summary>Bottom left.</summary>
    BottomLeft = 3,

    /// <summary>
    /// Top edge, horizontally centred. The horizontal margin is ignored, because there is no edge
    /// to keep a distance from.
    /// </summary>
    /// <remarks>
    /// For images a client crops on the sides. Jellyfin's episode list does exactly that: it
    /// shows a still in a container of its own proportions and takes the difference off the left
    /// and right, so a badge near either edge is cut in half however large the margin is. Centred,
    /// the crop can eat as much as it likes from both sides and the badge stays whole - only the
    /// vertical distance still has to fit.
    /// </remarks>
    TopCentre = 4,

    /// <summary>
    /// Bottom edge, horizontally centred. Same reasoning as <see cref="TopCentre"/>.
    /// </summary>
    BottomCentre = 5,
}
