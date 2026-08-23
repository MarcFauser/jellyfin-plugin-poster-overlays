using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.PosterOverlays.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.PosterOverlays;

/// <summary>
/// Entry point of the poster overlay plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        // Bring a configuration written before presets existed up to the current layout, once and
        // as early as possible: everything else in this plugin resolves a preset per item, and a
        // configuration that has not been migrated has no preset to resolve.
        if (Configuration.Migrate())
        {
            SaveConfiguration();
        }
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Poster Overlays";

    /// <inheritdoc />
    public override string Description =>
        "Draws a small badge onto the primary image - the edition, the resolution and the video " +
        "range - so that two entries of the same film can be told apart on the tile. The badge is " +
        "re-applied whenever a provider replaces the cover.";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("9cfb36f6-2afc-4009-ba14-8a8cad609904");

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.configPage.html",
                    GetType().Namespace),
            },
        };
    }
}
