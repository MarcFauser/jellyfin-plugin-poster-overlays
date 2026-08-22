namespace Jellyfin.Plugin.PosterOverlays.State;

/// <summary>
/// What the plugin remembers about one item between runs.
/// </summary>
/// <remarks>
/// The hash of the badged image is the load-bearing field. The image tag cannot be used for
/// this: it changes when the plugin itself uploads, so comparing tags cannot tell "a provider
/// replaced the cover" from "we badged it". Comparing the bytes can.
/// </remarks>
internal sealed class OverlayRecord
{
    /// <summary>
    /// Gets or sets the badge set that was drawn, as a stable key. A change here means the
    /// image has to be redrawn even though nobody replaced the cover.
    /// </summary>
    public string BadgeKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SHA-256 of the untouched original that was cached.
    /// </summary>
    public string OriginalHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SHA-256 of the image as Jellyfin stored it after the upload. This is
    /// what the next run compares the current image against.
    /// </summary>
    public string BadgedHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file extension of the cached original, including the dot.
    /// </summary>
    public string OriginalExtension { get; set; } = ".jpg";

    /// <summary>
    /// Gets or sets when this record was last written, as an ISO 8601 timestamp in UTC.
    /// </summary>
    public string UpdatedUtc { get; set; } = string.Empty;
}
