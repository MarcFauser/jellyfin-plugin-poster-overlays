using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PosterOverlays.State;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PosterOverlays;

/// <summary>
/// Re-badges an item as soon as Jellyfin reports that its image changed.
/// </summary>
/// <remarks>
/// This is what turns "Refresh metadata" in the item's own menu into a manual trigger for this
/// plugin. The web client's context menu is built from a hard-coded list in
/// <c>itemContextMenu.js</c> and cannot be extended by a server plugin, so reacting to the
/// entry that already exists is the closest thing to the button one would like to have.
/// <para>
/// It does not loop. The plugin's own write raises <see cref="ILibraryManager.ItemUpdated"/>
/// again, but the second pass finds the hash it just recorded, decides there is nothing to do
/// and writes nothing, so the chain ends after one turn. That is idempotence doing the work,
/// not a flag somebody has to remember to clear.
/// </para>
/// </remarks>
public sealed class OverlayRefreshWatcher : IHostedService, IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly ILogger<OverlayRefreshWatcher> _logger;
    private readonly ConcurrentDictionary<Guid, byte> _inFlight = new();
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="OverlayRefreshWatcher"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="providerManager">Instance of the <see cref="IProviderManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
    public OverlayRefreshWatcher(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        ILogger<OverlayRefreshWatcher> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemUpdated += OnItemUpdated;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemUpdated -= OnItemUpdated;
        await _shutdown.CancelAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _shutdown.Dispose();
    }

    private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        if (e is null || (e.UpdateReason & ItemUpdateType.ImageUpdate) == 0)
        {
            return;
        }

        if (e.Item is not Movie movie)
        {
            return;
        }

        var plugin = Plugin.Instance;
        if (plugin is null || !plugin.Configuration.Enabled || !plugin.Configuration.WatchForImageChanges)
        {
            return;
        }

        // Our own upload raises this event too. Skipping while the applier still has the item
        // open is what stops the watcher from reading a half-finished state and concluding the
        // image it is looking at is an untouched original.
        if (OverlayApplier.IsBusy(movie.Id))
        {
            return;
        }

        // The event is raised on Jellyfin's own thread while it is still finishing the update.
        // Doing the work here would block the refresh, so it is handed off - and the item is
        // marked in flight so a burst of events for one item does not start several passes.
        if (!_inFlight.TryAdd(movie.Id, 0))
        {
            return;
        }

        _ = Task.Run(() => HandleAsync(movie), _shutdown.Token);
    }

    private async Task HandleAsync(BaseItem item)
    {
        try
        {
            var plugin = Plugin.Instance;
            if (plugin is null)
            {
                return;
            }

            var store = OverlayStateStore.Shared(plugin.DataFolderPath);
            var applier = new OverlayApplier(_providerManager, _logger, plugin.Configuration, store);

            var outcome = await applier.ApplyAsync(item, _shutdown.Token).ConfigureAwait(false);
            if (outcome is OverlayOutcome.Unchanged or OverlayOutcome.Skipped or OverlayOutcome.NoImage)
            {
                return;
            }

            if (!plugin.Configuration.DryRun)
            {
                store.Flush();
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Poster overlays: {Name} was {Outcome} after its image changed.",
                    item.Name,
                    outcome);
            }
        }
        catch (OperationCanceledException)
        {
            // The server is shutting down. Nothing to report.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Poster overlays: reacting to the image change of {Name} failed.", item.Name);
        }
        finally
        {
            _inFlight.TryRemove(item.Id, out _);
        }
    }
}
