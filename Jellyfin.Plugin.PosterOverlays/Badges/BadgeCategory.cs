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

    /// <summary>
    /// The presentation format, currently 3D. Its own category rather than an edition, because
    /// it is a different question: a film can be an extended cut AND in 3D, and one must not
    /// push the other off the poster.
    /// </summary>
    Format = 3,

    /// <summary>A placeholder rip: CAM, TS, TC, SCR. Meant to be noticed.</summary>
    Source = 4,

    /// <summary>
    /// The best audio format the file carries: ATMOS, DTS-X, TRUEHD and so on.
    /// </summary>
    /// <remarks>
    /// Last on purpose, and drawn only where it tells two copies apart. Measured on the reference
    /// library: of 105 groups that share a film, 7 differ in nothing but the audio format - and
    /// 2,346 films would otherwise each carry a badge that says something true about a file
    /// nobody is comparing to anything.
    /// </remarks>
    Audio = 5,
}
