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
}
