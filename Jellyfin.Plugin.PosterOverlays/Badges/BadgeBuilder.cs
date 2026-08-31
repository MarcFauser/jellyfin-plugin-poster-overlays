using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.PosterOverlays.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
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
    /// <param name="config">The settings that are the same everywhere - the resolution ladder,
    /// the minimum rung, whether DV and HDR share a pill.</param>
    /// <param name="category">
    /// The policy for this kind of item: which badge kinds it may carry. Separate from the preset
    /// because it depends on the item, not on the look - a season has no use for an edition badge
    /// however it is drawn.
    /// </param>
    /// <param name="preset">The look, which also carries the order and the maximum count.</param>
    /// <param name="editionOverride">
    /// An edition badge forced by configuration, an empty string to suppress the edition badge,
    /// or null when nothing was configured for this item.
    /// </param>
    /// <returns>The result, never null.</returns>
    public static Built Build(BaseItem item, PluginConfiguration config, CategorySettings category, BadgePreset preset, string? editionOverride)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(preset);

        var parsed = ParseReleaseName(item);
        var ranges = TechnicalBadges.VideoRange(
            item.GetMediaStreams()?.FirstOrDefault(s => s.Type == MediaStreamType.Video)?.VideoRangeType.ToString(),
            config.MergeDolbyVisionAndHdr);

        // Sort before trimming, because the order is also the priority: what falls off the end
        // is decided by where the user put it.
        var badges = Order(Raw(item, config, category, editionOverride), preset.BadgeOrder);

        if (badges.Count > preset.MaxBadges)
        {
            badges = badges.Take(Math.Max(0, preset.MaxBadges)).ToList();
        }

        return new Built(
            badges,
            parsed.TitleTrusted,
            FolderClaimsHdr: FolderMentionsHdr(parsed.TagZone),
            StreamHasHdr: ranges.Count > 0,
            TagZone: parsed.TagZone);
    }

    /// <summary>
    /// The badges an item earns, in their natural order and untrimmed.
    /// </summary>
    /// <remarks>
    /// Split out from <see cref="Build"/> for the aggregator, which needs to know everything an
    /// episode offers before anything is dropped: a badge trimmed off one episode would look like
    /// a badge that episode does not have, and the parent would come out partial for no reason.
    /// </remarks>
    /// <param name="item">The library item.</param>
    /// <param name="config">The settings that are the same everywhere.</param>
    /// <param name="category">The policy for this kind of item.</param>
    /// <param name="editionOverride">A forced edition badge, an empty string to suppress, or null.</param>
    /// <returns>The badges, unordered and untrimmed.</returns>
    public static List<BadgeSpec> Raw(BaseItem item, PluginConfiguration config, CategorySettings category, string? editionOverride)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(category);

        var parsed = ParseReleaseName(item);

        var badges = new List<BadgeSpec>();

        if (category.AllowEdition)
        {
            // An override replaces the whole set rather than the first entry. It is how somebody
            // says "this folder means EXT whatever you read in it", and honouring it while still
            // appending a parsed REM would be answering a question that was not asked.
            if (editionOverride is not null)
            {
                if (editionOverride.Length > 0)
                {
                    badges.Add(new BadgeSpec(BadgeCategory.Edition, editionOverride));
                }
            }
            else
            {
                foreach (string edition in parsed.Editions)
                {
                    if (!string.IsNullOrEmpty(edition))
                    {
                        badges.Add(new BadgeSpec(BadgeCategory.Edition, edition));
                    }
                }
            }
        }

        var video = item.GetMediaStreams()?.FirstOrDefault(s => s.Type == MediaStreamType.Video);

        if (category.AllowResolution && video?.Width is int width)
        {
            string? resolution = TechnicalBadges.Resolution(width, config.ResolutionLadder, config.MinimumResolutionK);
            if (resolution is not null)
            {
                badges.Add(new BadgeSpec(BadgeCategory.Resolution, resolution));
            }
        }

        if (category.AllowVideoRange)
        {
            foreach (string range in TechnicalBadges.VideoRange(video?.VideoRangeType.ToString(), config.MergeDolbyVisionAndHdr))
            {
                badges.Add(new BadgeSpec(BadgeCategory.VideoRange, range));
            }
        }

        if (category.AllowFormat && parsed.Format is not null)
        {
            badges.Add(new BadgeSpec(BadgeCategory.Format, parsed.Format));
        }

        if (category.AllowSource && parsed.Source is not null)
        {
            badges.Add(new BadgeSpec(BadgeCategory.Source, parsed.Source));
        }

        return badges;
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
    /// <summary>
    /// Reads the release tags of an item from wherever that item keeps them.
    /// </summary>
    /// <remarks>
    /// A movie keeps them in its folder name. An episode keeps them in its file name, because a
    /// flattened season is one folder with every episode beside each other in it - so the folder
    /// is called <c>Season 01</c> and says nothing.
    /// <para>
    /// The file is tried first and the folder is the fallback, which covers the other layout
    /// without a setting: a series that was not flattened gives each episode its own release
    /// folder, and there the folder is exactly right. The fallback is safe in the flattened case
    /// too - <c>Season 01</c> tokenises to two words that match no rule.
    /// </para>
    /// <para>
    /// "Found nothing" and "found something" are kept whole rather than merged. Mixing a zone
    /// from the file with one from the folder would produce a tag zone that exists nowhere on
    /// disk, and that zone is also what the HDR cross-check reads.
    /// </para>
    /// </remarks>
    private static FolderNameParser.Result ParseReleaseName(BaseItem item)
    {
        if (item is Episode)
        {
            var fromFile = EpisodeFileNameParser.Parse(item.Path, item.Name);
            if (fromFile.Edition is not null || fromFile.Source is not null || fromFile.Format is not null)
            {
                return fromFile;
            }
        }

        return FolderNameParser.Parse(FolderName(item), item.Name, item.OriginalTitle);
    }

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
