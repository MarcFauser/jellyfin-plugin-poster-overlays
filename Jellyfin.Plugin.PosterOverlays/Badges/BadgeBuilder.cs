using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.PosterOverlays.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.PosterOverlays.Badges;

/// <summary>
/// Turns a library item into the list of badges that belongs on its poster.
/// </summary>
/// <remarks>
/// A thin adapter on purpose: everything that can be decided from strings and numbers lives in
/// <see cref="FolderNameParser"/> and <see cref="TechnicalBadges"/>, which are covered by tests.
/// This class only fetches the inputs out of Jellyfin's object model.
/// </remarks>
internal static class BadgeBuilder
{
    /// <summary>
    /// Builds the badge list for an item.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <param name="config">The settings.</param>
    /// <param name="editionOverride">
    /// An edition badge forced by configuration, an empty string to suppress the edition badge,
    /// or null when nothing was configured for this item.
    /// </param>
    /// <returns>The result, never null.</returns>
    public static Built Build(BaseItem item, PluginConfiguration config, string? editionOverride)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(config);

        string? folderName = FolderName(item);
        var parsed = FolderNameParser.Parse(folderName, item.Name, item.OriginalTitle);

        var badges = new List<BadgeSpec>();

        if (config.ShowEditionBadges)
        {
            string? edition = editionOverride ?? parsed.Edition;
            if (!string.IsNullOrEmpty(edition))
            {
                badges.Add(new BadgeSpec(BadgeCategory.Edition, edition));
            }
        }

        var video = item.GetMediaStreams()?.FirstOrDefault(s => s.Type == MediaStreamType.Video);

        if (config.ShowResolutionBadges && video?.Width is int width)
        {
            string? resolution = TechnicalBadges.Resolution(width, config.ResolutionLadder, config.MinimumResolutionK);
            if (resolution is not null)
            {
                badges.Add(new BadgeSpec(BadgeCategory.Resolution, resolution));
            }
        }

        var ranges = TechnicalBadges.VideoRange(video?.VideoRangeType.ToString(), config.MergeDolbyVisionAndHdr);
        if (config.ShowVideoRangeBadges)
        {
            foreach (string range in ranges)
            {
                badges.Add(new BadgeSpec(BadgeCategory.VideoRange, range));
            }
        }

        if (config.ShowSourceBadges && parsed.Source is not null)
        {
            badges.Add(new BadgeSpec(BadgeCategory.Source, parsed.Source));
        }

        // Sort before trimming, because the order is also the priority: what falls off the end
        // is decided by where the user put it.
        badges = Order(badges, config.BadgeOrder);

        if (badges.Count > config.MaxBadges)
        {
            badges = badges.Take(Math.Max(0, config.MaxBadges)).ToList();
        }

        return new Built(
            badges,
            parsed.TitleTrusted,
            FolderClaimsHdr: FolderMentionsHdr(parsed.TagZone),
            StreamHasHdr: ranges.Count > 0,
            TagZone: parsed.TagZone);
    }

    /// <summary>
    /// Puts the badges into the configured order.
    /// </summary>
    /// <remarks>
    /// A category the list does not mention keeps its natural position at the end rather than
    /// disappearing: a typo in the setting should cost the order, not the badge.
    /// </remarks>
    /// <param name="badges">The badges in their natural order.</param>
    /// <param name="order">Comma separated category names.</param>
    /// <returns>The badges, sorted.</returns>
    public static List<BadgeSpec> Order(IReadOnlyList<BadgeSpec> badges, string? order)
    {
        ArgumentNullException.ThrowIfNull(badges);

        var rank = new Dictionary<BadgeCategory, int>();
        int next = 0;
        foreach (string name in (order ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse(name, ignoreCase: true, out BadgeCategory category) && !rank.ContainsKey(category))
            {
                rank[category] = next++;
            }
        }

        foreach (BadgeCategory category in Enum.GetValues<BadgeCategory>())
        {
            if (!rank.ContainsKey(category))
            {
                rank[category] = 100 + (int)category;
            }
        }

        return badges.OrderBy(b => rank[b.Category]).ToList();
    }

    /// <summary>
    /// Builds the key that says "the same badges as last time".
    /// </summary>
    /// <param name="badges">The badges.</param>
    /// <returns>The key.</returns>
    public static string KeyOf(IReadOnlyList<BadgeSpec> badges)
    {
        ArgumentNullException.ThrowIfNull(badges);
        return string.Join('|', badges.Select(b => b.Key()));
    }

    /// <summary>
    /// Reads the release folder name of an item.
    /// </summary>
    /// <remarks>
    /// <c>Path</c> of a movie is the video file, so the folder is its parent - which is what
    /// <c>ContainingFolderPath</c> returns, and for a disc folder it returns the folder itself.
    /// An item in a mixed folder shares that folder with other films, so its name says nothing
    /// about this item and is not used.
    /// </remarks>
    private static string? FolderName(BaseItem item)
    {
        if (item.IsInMixedFolder)
        {
            return null;
        }

        string? folder = item.ContainingFolderPath;
        return string.IsNullOrEmpty(folder) ? null : Path.GetFileName(folder);
    }

    private static bool FolderMentionsHdr(string tagZone)
    {
        foreach (string token in tagZone.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token is "hdr" or "hdr10" or "hdr10plus" or "dv" or "dovi" or "hlg")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What was built, and what was noticed along the way.
    /// </summary>
    /// <param name="Badges">The badges, in stacking order and already trimmed.</param>
    /// <param name="TitleTrusted">False when the item has no metadata match.</param>
    /// <param name="FolderClaimsHdr">
    /// True when the folder name advertises HDR or Dolby Vision. Compared against the stream so
    /// a disagreement can be reported instead of quietly resolved.
    /// </param>
    /// <param name="StreamHasHdr">True when the video stream really carries HDR or Dolby Vision.</param>
    /// <param name="TagZone">The normalised text the edition rules were applied to.</param>
    internal sealed record Built(
        IReadOnlyList<BadgeSpec> Badges,
        bool TitleTrusted,
        bool FolderClaimsHdr,
        bool StreamHasHdr,
        string TagZone);
}
