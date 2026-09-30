using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PosterOverlays.State;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PosterOverlays.Tasks;

/// <summary>
/// Works the plugin's records out again from its cached originals, after the state file was lost
/// or damaged.
/// </summary>
/// <remarks>
/// The scheduled-task shell around <see cref="StateRebuilder"/>, which holds the reasoning. Never
/// scheduled by default: it answers one specific failure, and it is meant to be started by hand -
/// with the dry run on first, so the owner reads the list of what it would do before it does it.
/// </remarks>
public sealed class RebuildStateTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly ILocalizationManager _localization;
    private readonly ILogger<RebuildStateTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RebuildStateTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="providerManager">Instance of the <see cref="IProviderManager"/> interface.</param>
    /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
    public RebuildStateTask(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        ILocalizationManager localization,
        ILogger<RebuildStateTask> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _localization = localization;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Rebuild poster overlay state";

    /// <inheritdoc />
    public string Description =>
        "Works the plugin's records out again from the cached originals, after the state file was lost or "
        + "damaged. A record is only rebuilt where drawing today's badges onto the cached original gives the "
        + "cover on the item again; everything else is listed by name and left untouched. Respects the dry run "
        + "switch - run it with the dry run on first.";

    /// <inheritdoc />
    public string Key => "PosterOverlaysRebuildState";

    /// <inheritdoc />
    public string Category => "Poster Overlays";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            _logger.LogWarning("Poster overlays: the plugin instance is not available, nothing was done.");
            return;
        }

        var config = plugin.Configuration;
        var rebuilder = new StateRebuilder(
            plugin.DataFolderPath,
            store => new OverlayApplier(_providerManager, _libraryManager, _logger, config, store, _localization),
            _libraryManager.GetItemById,
            _logger,
            config.DryRun,
            () => DateTime.UtcNow);

        var report = await rebuilder.RunAsync(progress, cancellationToken).ConfigureAwait(false);
        rebuilder.Log(report);
        progress.Report(100);
    }
}
