using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.PosterOverlays.Models;

/// <summary>
/// A short answer to "what is this plugin currently doing".
/// </summary>
public class OverlayStatusDto
{
    /// <summary>
    /// Gets or sets how many items carry a badge the plugin knows about.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int BadgedItems { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the plugin only reports.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool DryRun { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the plugin reacts to image changes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool WatchForImageChanges { get; set; }
}
