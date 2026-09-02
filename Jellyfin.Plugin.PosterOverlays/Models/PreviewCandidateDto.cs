using System;

namespace Jellyfin.Plugin.PosterOverlays.Models;

/// <summary>
/// An item the settings page can offer as a preview subject.
/// </summary>
/// <remarks>
/// These come from the plugin's own records rather than from a library query, and that is the
/// point: most of the library carries no badge, so a random item would usually preview as an
/// untouched poster and look like a fault.
/// </remarks>
public class PreviewCandidateDto
{
    /// <summary>
    /// Gets or sets the item id.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the item name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the production year, when the item has one.
    /// </summary>
    public int? ProductionYear { get; set; }

    /// <summary>
    /// Gets or sets which category the item falls into - Movie, Series, Season or Episode.
    /// </summary>
    /// <remarks>
    /// Shown beside the name so the list is readable: three entries called "Pilot" are told apart
    /// by nothing else, and the category also says which preset the preview will use.
    /// </remarks>
    public string? Kind { get; set; }
}
