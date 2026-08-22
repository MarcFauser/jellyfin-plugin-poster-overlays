using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.PosterOverlays.Models;

/// <summary>
/// What happened to one item.
/// </summary>
/// <remarks>
/// Every property is marked so it is sent even when it is null. Jellyfin's server-wide
/// serializer setting is <c>WhenWritingNull</c>, which drops null fields from the payload
/// entirely - and a field that is missing looks exactly like a field the plugin does not know
/// about. On this plugin's own routes, a missing field therefore means something is wrong,
/// rather than merely that nothing was set.
/// </remarks>
public class OverlayResultDto
{
    /// <summary>
    /// Gets or sets the item id.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the item name.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets what was done, as the name of an outcome.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Outcome { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this was only a report and nothing was changed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool DryRun { get; set; }
}
