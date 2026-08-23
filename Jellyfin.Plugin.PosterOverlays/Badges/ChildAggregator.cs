using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.PosterOverlays.Configuration;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.PosterOverlays.Badges;

/// <summary>
/// Works out what a series or a season may claim, from the episodes underneath it.
/// </summary>
/// <remarks>
/// A series has no resolution of its own, so anything shown on its tile is a statement about its
/// episodes - and there are two very different ways for those to disagree.
/// <list type="bullet">
/// <item>
/// <b>A choice of copies.</b> The same episode exists more than once, in different quality.
/// Nothing is missing; you simply pick. Measured on the reference library: all 28 episodes of one
/// series are present as 1080p DV and as 4K DV, and calling that series "mixed" would be false -
/// the whole thing <i>is</i> available in 4K.
/// </item>
/// <item>
/// <b>Genuine variation.</b> Different episodes are genuinely different: season one exists only
/// in 1080p SDR while season two is also there in 4K DV.
/// </item>
/// </list>
/// The rule that separates them is one step: <b>collapse every episode to what its best copy
/// offers, then require agreement.</b> Without the collapse the first case reads as mixed, which
/// is the wrong answer to the question anybody is actually asking.
/// <para>
/// The result is binary. A series with 7 of 8 episodes in 4K and one with 22 of 52 are both simply
/// "not uniform"; a threshold would lie at exactly the point where somebody relies on it.
/// </para>
/// <para>
/// And a series whose episodes carry nothing notable gets <b>no badge at all</b>, not a partial
/// one. The partial state only appears where something worth showing actually varies.
/// </para>
/// </remarks>
internal static class ChildAggregator
{
    /// <summary>
    /// Builds the badge list for a parent from the episodes below it.
    /// </summary>
    /// <param name="episodes">Every episode row underneath, duplicates included.</param>
    /// <param name="config">The settings that are the same everywhere.</param>
    /// <param name="category">The policy of the parent's category.</param>
    /// <param name="preset">The look, for the order and the maximum count.</param>
    /// <returns>The badges, ordered and trimmed, each carrying its availability.</returns>
    public static List<BadgeSpec> Aggregate(
        IReadOnlyList<BaseItem> episodes,
        PluginConfiguration config,
        CategorySettings category,
        BadgePreset preset)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(preset);

        return Combine(
            episodes.Select(e => new Copy(EpisodeKey(e), BadgeBuilder.Raw(e, config, category, null))).ToList(),
            preset);
    }

    /// <summary>
    /// The part of the aggregation that has no Jellyfin in it: collapse, then agree.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Aggregate"/> so it can be tested without a media source manager.
    /// The interesting decisions all live here.
    /// </remarks>
    /// <param name="copies">One entry per file, several sharing a slot when an episode exists twice.</param>
    /// <param name="preset">The look, for the order and the maximum count.</param>
    /// <returns>The badges, ordered and trimmed.</returns>
    public static List<BadgeSpec> Combine(IReadOnlyList<Copy> copies, BadgePreset preset)
    {
        ArgumentNullException.ThrowIfNull(copies);
        ArgumentNullException.ThrowIfNull(preset);

        if (copies.Count == 0)
        {
            return [];
        }

        // Step one: collapse. Every distinct episode contributes the union of what its copies
        // offer, because "is this episode available in 4K" is answered by any copy, not by a
        // copy chosen in advance.
        var perEpisode = new Dictionary<string, Dictionary<BadgeCategory, HashSet<string>>>(StringComparer.Ordinal);

        foreach (var copy in copies)
        {
            if (!perEpisode.TryGetValue(copy.Slot, out var labels))
            {
                labels = [];
                perEpisode[copy.Slot] = labels;
            }

            foreach (var badge in copy.Badges)
            {
                if (!labels.TryGetValue(badge.Category, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    labels[badge.Category] = set;
                }

                set.Add(badge.Text);
            }
        }

        // Step two: one badge per category, the best label that occurs anywhere, and uniform only
        // when every distinct episode offers that same label.
        var result = new List<BadgeSpec>();

        foreach (BadgeCategory kind in Enum.GetValues<BadgeCategory>())
        {
            string? best = null;
            int bestRank = int.MinValue;

            foreach (var labels in perEpisode.Values)
            {
                if (!labels.TryGetValue(kind, out var set))
                {
                    continue;
                }

                foreach (string label in set)
                {
                    int rank = Rank(kind, label);
                    if (rank > bestRank)
                    {
                        bestRank = rank;
                        best = label;
                    }
                }
            }

            if (best is null)
            {
                continue;
            }

            bool everywhere = perEpisode.Values.All(labels =>
                labels.TryGetValue(kind, out var set) && set.Contains(best));

            result.Add(new BadgeSpec(
                kind,
                best,
                everywhere ? BadgeAvailability.Uniform : BadgeAvailability.Partial));
        }

        var ordered = BadgeBuilder.Order(result, preset.BadgeOrder);
        return ordered.Count > preset.MaxBadges
            ? ordered.Take(Math.Max(0, preset.MaxBadges)).ToList()
            : ordered;
    }

    /// <summary>
    /// What counts as one episode, however many files carry it.
    /// </summary>
    /// <remarks>
    /// Season and episode number, because that is what makes two rows the same episode - the two
    /// copies live in different folders and have different ids. A row without numbers cannot be
    /// paired with anything, so it stands for itself under its own id rather than being lumped in
    /// with every other unnumbered row.
    /// </remarks>
    private static string EpisodeKey(BaseItem episode) =>
        episode.ParentIndexNumber is int season && episode.IndexNumber is int number
            ? string.Create(CultureInfo.InvariantCulture, $"{season}:{number}")
            : "id:" + episode.Id.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>
    /// Which of two labels in the same category is the better one.
    /// </summary>
    /// <remarks>
    /// Only needed when a series genuinely spans rungs - some episodes 4K and some 8K. Showing the
    /// higher one marked as partial says "this is available, but not throughout", which is the
    /// useful statement; showing the lower one would understate what is there.
    /// </remarks>
    private static int Rank(BadgeCategory kind, string label)
    {
        if (kind == BadgeCategory.Resolution)
        {
            // "4K", "8K", "16K" - the number is the rank, and anything unparseable sorts last
            // rather than throwing.
            string digits = new(label.TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int k) ? k : -1;
        }

        if (kind == BadgeCategory.VideoRange)
        {
            return label switch
            {
                "DV HDR+" => 5,
                "DV HDR" => 4,
                "DV" => 3,
                "HDR+" => 2,
                "HDR" => 1,
                _ => 0,
            };
        }

        // Edition, format and source have no order worth inventing. The first one found wins, and
        // consistently so, because the label set is walked in a stable order.
        return 0;
    }

    /// <summary>
    /// One file's worth of badges, and which episode that file is a copy of.
    /// </summary>
    /// <param name="Slot">Season and episode number - two rows with the same slot are the same episode.</param>
    /// <param name="Badges">What this particular file earns.</param>
    internal sealed record Copy(string Slot, IReadOnlyList<BadgeSpec> Badges);
}
