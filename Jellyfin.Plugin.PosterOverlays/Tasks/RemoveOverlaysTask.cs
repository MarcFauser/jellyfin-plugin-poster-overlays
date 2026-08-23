using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PosterOverlays.State;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PosterOverlays.Tasks;

/// <summary>
/// Puts every cached original back and forgets the items.
/// </summary>
/// <remarks>
/// The way out. Without it the badges would outlive the plugin, because uninstalling it leaves
/// the uploaded images exactly where they are. It has no default trigger: this runs when the
/// user asks for it and never on its own.
/// </remarks>
public sealed class RemoveOverlaysTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly ILogger<RemoveOverlaysTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoveOverlaysTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="providerManager">Instance of the <see cref="IProviderManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
    public RemoveOverlaysTask(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        ILogger<RemoveOverlaysTask> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Remove poster overlays";

    /// <inheritdoc />
    public string Key => "PosterOverlaysRemove";

    /// <inheritdoc />
    public string Description =>
        "Restores the cached original cover of every item this plugin has badged, and forgets it. "
        + "Run this before uninstalling the plugin - the badged images would otherwise stay.";

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

        var store = OverlayStateStore.Shared(plugin.DataFolderPath);
        var applier = new OverlayApplier(_providerManager, _libraryManager, _logger, plugin.Configuration, store);

        var ids = store.KnownItemIds().ToList();
        int restored = 0;
        int gone = 0;
        int failed = 0;

        for (int i = 0; i < ids.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = _libraryManager.GetItemById(Guid.Parse(ids[i]));
            if (item is null)
            {
                // The item is gone from the library. Drop the record and its cached original,
                // otherwise the data folder grows forever.
                store.Forget(ids[i]);
                gone++;
            }
            else
            {
                try
                {
                    if (await applier.RestoreAsync(item, cancellationToken).ConfigureAwait(false))
                    {
                        restored++;
                    }
                    else
                    {
                        failed++;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Poster overlays: restoring {Name} failed.", item.Name);
                    failed++;
                }
            }

            progress.Report((i + 1) * 100.0 / Math.Max(1, ids.Count));
        }

        store.Flush();
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Poster overlays: {Restored} originals restored, {Gone} records dropped for items that no longer exist, "
                + "{Failed} could not be restored.",
                restored,
                gone,
                failed);
        }

        progress.Report(100);
    }
}
