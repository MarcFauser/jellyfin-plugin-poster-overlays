using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.PosterOverlays.Configuration;

/// <summary>
/// The presets that ship with the plugin. They live in code, not in the configuration, and cannot
/// be edited - the way to change one is to copy it under a new name.
/// </summary>
/// <remarks>
/// Read-only is enforced structurally rather than promised: every accessor hands out a fresh
/// instance, so a caller that mutates what it got has mutated a copy and nothing else. A flag
/// somebody has to remember to check would be the weaker arrangement.
/// <para>
/// <b>A built-in whose rendering would change gets a new id and the old one stays.</b> Otherwise
/// "read-only" is only half a promise - protected against the user, but not against us at the next
/// release, and a category pointing at it would silently start drawing differently.
/// </para>
/// </remarks>
public static class BuiltInPresets
{
    // Counted rather than random, because these are never generated and never collide - they are
    // constants. The payoff is legibility: a category pointing at ...0002 in the configuration
    // file or in an exported preset is visibly pointing at something shipped, where a random id
    // would look exactly like one the user made.
    //
    // Numbering starts at 1 on purpose. All zeroes is Guid.Empty, which is also what an unset
    // PresetId holds - and that has to keep dangling so the fallback fires loudly instead of
    // silently resolving to whichever built-in happened to be number zero.

    /// <summary>The id of the built-in preset for films.</summary>
    public static readonly Guid MovieId = new("00000000-0000-0000-0000-000000000001");

    /// <summary>The id of the built-in preset for series.</summary>
    public static readonly Guid SeriesId = new("00000000-0000-0000-0000-000000000002");

    /// <summary>The id of the built-in preset for seasons.</summary>
    public static readonly Guid SeasonId = new("00000000-0000-0000-0000-000000000003");

    /// <summary>The id of the built-in preset for episodes.</summary>
    public static readonly Guid EpisodeId = new("00000000-0000-0000-0000-000000000004");

    /// <summary>
    /// Gets every built-in preset, as fresh instances.
    /// </summary>
    /// <returns>The built-ins, in the order they should be listed.</returns>
    public static Collection<BadgePreset> All() =>
    [
        Movie(),
        Series(),
        Season(),
        Episode(),
    ];

    /// <summary>
    /// Says whether an id belongs to a built-in.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <returns>True when it does.</returns>
    public static bool IsBuiltIn(Guid id) =>
        id == MovieId || id == SeriesId || id == SeasonId || id == EpisodeId;

    /// <summary>
    /// Returns a built-in by id.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <returns>A fresh instance, or null when the id is not a built-in.</returns>
    public static BadgePreset? Get(Guid id)
    {
        if (id == MovieId)
        {
            return Movie();
        }

        if (id == SeriesId)
        {
            return Series();
        }

        if (id == SeasonId)
        {
            return Season();
        }

        if (id == EpisodeId)
        {
            return Episode();
        }

        return null;
    }

    /// <summary>
    /// The built-in that a category falls back to when its preset cannot be found.
    /// </summary>
    /// <param name="target">The kind of item.</param>
    /// <returns>The id of the matching built-in.</returns>
    public static Guid DefaultFor(BadgeTarget target) => target switch
    {
        BadgeTarget.Series => SeriesId,
        BadgeTarget.Season => SeasonId,
        BadgeTarget.Episode => EpisodeId,
        _ => MovieId,
    };

    /// <summary>
    /// Portrait, no completeness colours: a film is one file and would always be "complete".
    /// These are the values that were chosen on rendered comparisons at real card size.
    /// </summary>
    /// <returns>The preset.</returns>
    private static BadgePreset Movie() => new()
    {
        Id = MovieId,
        Name = "Movie",
    };

    /// <summary>
    /// Portrait, with the traffic light on: a series has no resolution of its own, so what it
    /// shows is aggregated from its episodes and can be partial.
    /// </summary>
    /// <returns>The preset.</returns>
    private static BadgePreset Series() => new()
    {
        Id = SeriesId,
        Name = "Series",
        CompletenessColours = true,
        Glow = true,
        PartialMarker = PartialMarker.Diagonal,
    };

    /// <summary>
    /// The same as a series. Kept separate so the two can drift apart without a copy.
    /// </summary>
    /// <returns>The preset.</returns>
    private static BadgePreset Season() => new()
    {
        Id = SeasonId,
        Name = "Season",
        CompletenessColours = true,
        Glow = true,
        PartialMarker = PartialMarker.Diagonal,
    };

    /// <summary>
    /// Landscape. The pill height cannot be copied from the portrait presets - at the same
    /// displayed width a 16:9 still is a little over a third as tall as a 2:3 poster, so the same
    /// percentage gives a badge a third the size.
    /// </summary>
    /// <returns>The preset.</returns>
    private static BadgePreset Episode() => new()
    {
        Id = EpisodeId,
        Name = "Episode",
        PillHeightPercent = 10.0,
        VerticalMarginPercent = 3.5,
        HorizontalMarginPercent = 2.0,
    };
}
