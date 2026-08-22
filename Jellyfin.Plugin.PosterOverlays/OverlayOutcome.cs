namespace Jellyfin.Plugin.PosterOverlays;

/// <summary>
/// What happened to one item during a run.
/// </summary>
internal enum OverlayOutcome
{
    /// <summary>Excluded by configuration.</summary>
    Skipped,

    /// <summary>The item has no primary image to draw on.</summary>
    NoImage,

    /// <summary>Nothing to do: the current image is ours and carries the right badges.</summary>
    Unchanged,

    /// <summary>The item had no record yet and was badged for the first time.</summary>
    FirstRun,

    /// <summary>
    /// The image on the item is not the one this plugin uploaded, so a provider delivered a new
    /// cover. It was cached as the new original and badged again. Noticing this is half of what
    /// the plugin is for.
    /// </summary>
    CoverReplaced,

    /// <summary>The image is still ours but the badge set changed, so it was redrawn.</summary>
    BadgesChanged,

    /// <summary>There is nothing left to badge, so the cached original was put back.</summary>
    Restored,

    /// <summary>
    /// The badge set changed but the cached original is gone. Nothing was drawn: painting onto
    /// the badged image would stack a badge on a badge, and that does not undo itself.
    /// </summary>
    OriginalMissing,

    /// <summary>
    /// The cached original is not the image the record describes - something wrote over it.
    /// Nothing was drawn, because the only safe original left is the one a provider can deliver.
    /// The repair task collects these.
    /// </summary>
    CacheInconsistent,

    /// <summary>Something went wrong; the log carries the reason.</summary>
    Failed,
}
