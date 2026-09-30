using Jellyfin.Plugin.PosterOverlays.State;

namespace Jellyfin.Plugin.PosterOverlays;

/// <summary>
/// What the rebuild found out about one cached original.
/// </summary>
internal enum RebuildFinding
{
    /// <summary>
    /// The cover on the item is the cached original with today's badges drawn on it, so the record
    /// could be worked out again.
    /// </summary>
    Ours,

    /// <summary>
    /// The item shows the cached original itself, unbadged. No record is needed, and the cached copy
    /// is redundant.
    /// </summary>
    ShowsOriginal,

    /// <summary>
    /// The cover matches neither. Could be a cover a provider delivered after the record was lost,
    /// could be badges drawn with settings that have changed since - the rebuild cannot tell, so it
    /// touches nothing.
    /// </summary>
    Unmatched,

    /// <summary>The item has no primary image at all.</summary>
    NoImage,
}

/// <summary>
/// The verdict on one cached original.
/// </summary>
/// <param name="Finding">What was found.</param>
/// <param name="Record">The reconstructed record, for <see cref="RebuildFinding.Ours"/> only.</param>
/// <param name="ByteForByte">
/// True when the match was exact; false when it rested on <c>ImageComparison.SamePicture</c>.
/// </param>
/// <param name="Reason">Why, in words that belong in a log line.</param>
internal sealed record RebuildVerdict(RebuildFinding Finding, OverlayRecord? Record, bool ByteForByte, string Reason);
