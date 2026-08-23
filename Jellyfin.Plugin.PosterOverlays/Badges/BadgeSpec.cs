using System;

namespace Jellyfin.Plugin.PosterOverlays.Badges;

/// <summary>
/// One badge to be drawn: what it says and which category it belongs to.
/// </summary>
/// <param name="Category">The category, which decides colour and stacking order.</param>
/// <param name="Text">The label, always upper case and two to six characters.</param>
/// <param name="Availability">
/// Whether the claim holds for everything underneath. Always
/// <see cref="BadgeAvailability.NotApplicable"/> for a film or an episode, which are one file each.
/// </param>
internal sealed record BadgeSpec(BadgeCategory Category, string Text, BadgeAvailability Availability = BadgeAvailability.NotApplicable)
{
    /// <summary>
    /// Gets a stable key for the badge set, used to tell "the same badges as last time" from a
    /// change without comparing images.
    /// </summary>
    /// <remarks>
    /// The availability is part of it. A series that gains its last missing 4K episode keeps the
    /// same label and has to be redrawn all the same, because the badge changes colour.
    /// </remarks>
    /// <returns>The key.</returns>
    public string Key() => Availability == BadgeAvailability.NotApplicable
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(int)Category}:{Text}")
        : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(int)Category}:{Text}:{(int)Availability}");
}
