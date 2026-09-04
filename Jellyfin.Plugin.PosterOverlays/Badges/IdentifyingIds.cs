using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.PosterOverlays.Badges;

/// <summary>
/// Picks the provider ids that say <b>which title this is</b>, out of everything an item carries.
/// </summary>
/// <remarks>
/// <b>Not every provider id identifies the item.</b> Some describe what it belongs to, and the one
/// that caused this class is <c>TmdbCollection</c>: on the reference library all fifteen Star Wars
/// films carry <c>TmdbCollection=10</c>. Searching for peers by "any shared provider id" therefore
/// returned the whole series as copies of one film, and an audio badge appeared on films that have
/// no second copy at all - measured, 2 of 25 single-copy films picked at random.
/// <para>
/// A positive list rather than "everything except collections". An unknown id might identify a
/// title or might group it, and the failure modes are not symmetric: missing a peer costs one
/// badge that would have helped, while a wrong peer puts a badge on a film that has no twin and
/// quietly contradicts what the badge is for.
/// </para>
/// </remarks>
internal static class IdentifyingIds
{
    /// <summary>
    /// The keys that name a title. <c>Custom</c> is in the list because an NFO can set it to
    /// override Jellyfin's grouping, which makes it the most deliberate identifier of all.
    /// </summary>
    private static readonly string[] Identifying = ["Imdb", "Tmdb", "Tvdb", "Custom"];

    /// <summary>
    /// Keeps the entries whose key identifies the title.
    /// </summary>
    /// <param name="providerIds">Everything the item carries.</param>
    /// <returns>The identifying subset, possibly empty. Never null.</returns>
    public static Dictionary<string, string> From(IReadOnlyDictionary<string, string>? providerIds)
    {
        var kept = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (providerIds is null)
        {
            return kept;
        }

        foreach (var pair in providerIds)
        {
            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            foreach (string key in Identifying)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    kept[pair.Key] = pair.Value;
                    break;
                }
            }
        }

        return kept;
    }
}
