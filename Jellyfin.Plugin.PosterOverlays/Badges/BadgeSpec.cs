using System;

namespace Jellyfin.Plugin.PosterOverlays.Badges;

/// <summary>
/// One badge to be drawn: what it says and which category it belongs to.
/// </summary>
/// <param name="Category">The category, which decides colour and stacking order.</param>
/// <param name="Text">The label, always upper case and two to six characters.</param>
internal sealed record BadgeSpec(BadgeCategory Category, string Text)
{
    /// <summary>
    /// Gets a stable key for the badge set, used to tell "the same badges as last time" from a
    /// change without comparing images.
    /// </summary>
    /// <returns>The key.</returns>
    public string Key() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"{(int)Category}:{Text}");
}
