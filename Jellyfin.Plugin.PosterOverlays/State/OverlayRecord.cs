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
    /// Gets or sets the appearance the badges were drawn with - style, corner, direction and
    /// every geometry percentage.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="BadgeKey"/> because the two answer different questions. The badge
    /// key says <em>which</em> badges belong on the poster; this says <em>how</em> they look. A
    /// larger pill or a different corner leaves the badge key untouched, so without this the run
    /// would report the item as already correct and never redraw it.
    /// <para>
    /// An empty value means the record predates this field. That compares as different from any
    /// real key, so the item is redrawn once - which is right: nobody knows what it was drawn
    /// with.
    /// </para>
    /// </remarks>
    public string LookKey { get; set; } = string.Empty;

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

    /// <summary>
    /// Gets or sets the hash of an image the plugin was about to upload, written down before the
    /// upload started. Null when no upload is in flight.
    /// </summary>
    /// <remarks>
    /// <b>This closes the window between the upload and the record.</b> The record used to be
    /// written only after the upload had returned, so a crash in between left the item carrying a
    /// badged image the record knew nothing about - and the next run, seeing an image that matched
    /// nothing it had uploaded, took it for a new cover from a provider, cached it as the original
    /// and drew a badge on top of it. With the hash announced beforehand, the next run recognises
    /// the image as its own and only has to confirm what already happened.
    /// <para>
    /// It can be announced because Jellyfin stores an uploaded image byte for byte: read in
    /// <c>ImageSaver.SaveImage</c> on <c>release-10.11.z</c> and on <c>master</c>, the stream goes
    /// through <c>CopyToAsync</c> into the target file and nothing re-encodes it.
    /// </para>
    /// <para>
    /// The three <c>Pending</c> fields are kept apart from <see cref="BadgedHash"/>,
    /// <see cref="BadgeKey"/> and <see cref="LookKey"/> on purpose. Those describe what is known to
    /// be on the item; these describe an attempt. Mixing them would let a crash before the upload
    /// claim the new badges for the old image, and the next run would call that image correct.
    /// </para>
    /// </remarks>
    public string? PendingBadgedHash { get; set; }

    /// <summary>
    /// Gets or sets the badge key of the image announced in <see cref="PendingBadgedHash"/>.
    /// </summary>
    public string? PendingBadgeKey { get; set; }

    /// <summary>
    /// Gets or sets the look key of the image announced in <see cref="PendingBadgedHash"/>.
    /// </summary>
    public string? PendingLookKey { get; set; }
}
