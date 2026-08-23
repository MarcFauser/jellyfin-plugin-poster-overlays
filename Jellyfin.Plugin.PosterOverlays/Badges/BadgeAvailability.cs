namespace Jellyfin.Plugin.PosterOverlays.Badges;

/// <summary>
/// Whether what a badge claims holds for everything underneath it.
/// </summary>
/// <remarks>
/// Only series and seasons can be anything but <see cref="NotApplicable"/>. A film is one file and
/// an episode is one file, so there is nothing to aggregate and nothing to be partial about.
/// <para>
/// The state is deliberately binary. A series with 7 of 8 episodes in 4K and one with 22 of 52 are
/// both simply "not uniform"; a threshold such as "90 % counts as complete" would lie at exactly
/// the point where somebody is relying on it.
/// </para>
/// </remarks>
internal enum BadgeAvailability
{
    /// <summary>Nothing was aggregated, so the question does not arise.</summary>
    NotApplicable,

    /// <summary>Every child agrees, once duplicate copies of one child are collapsed to the best.</summary>
    Uniform,

    /// <summary>Some children have it and some do not.</summary>
    Partial,
}
