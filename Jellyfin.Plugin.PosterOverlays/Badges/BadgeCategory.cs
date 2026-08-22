namespace Jellyfin.Plugin.PosterOverlays.Badges;

/// <summary>
/// What a badge says about the item. One badge per category is drawn at most, and the order of
/// the values is the order the badges are stacked in.
/// </summary>
internal enum BadgeCategory
{
    /// <summary>The cut of the film, parsed from the folder name. The actual discriminator.</summary>
    Edition = 0,

    /// <summary>The pixel width, expressed as 4K, 8K and so on.</summary>
    Resolution = 1,

    /// <summary>Dolby Vision and HDR, taken from the video stream.</summary>
    VideoRange = 2,

    /// <summary>A placeholder rip: CAM, TS, TC, SCR. Meant to be noticed.</summary>
    Source = 3,
}
