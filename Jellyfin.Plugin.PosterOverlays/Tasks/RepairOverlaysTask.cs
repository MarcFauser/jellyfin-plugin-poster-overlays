using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PosterOverlays.State;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PosterOverlays.Tasks;

/// <summary>
/// Finds items whose cached original is not what the record says it is, and fetches a fresh
/// cover from the metadata provider for them.
/// </summary>
/// <remarks>
/// This exists because of a real failure in the plugin's first release, not as a precaution.
/// The scheduled task and the image-change watcher each kept their own state and the task only
/// wrote its records at the end of a run, so the watcher saw an item the task had just badged,
/// found no record, cached the badged image as the "original" and drew a second badge over it.
/// Two identical badge stacks land on the same pixels and are invisible, so nothing looked
/// wrong - but the cached original was no longer an original, and every later run would have
/// added another layer.
/// <para>
/// A badged image cannot be un-badged, so the only true original left is the one the provider
/// still has. Only the primary image is replaced; nothing else about the item is touched.
/// </para>
/// </remarks>
public sealed class RepairOverlaysTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly ILogger<RepairOverlaysTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RepairOverlaysTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="providerManager">Instance of the <see cref="IProviderManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
    public RepairOverlaysTask(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        ILogger<RepairOverlaysTask> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Repair poster overlays";

    /// <inheritdoc />
    public string Description =>
        "Finds items whose cached original was overwritten and fetches a fresh primary image from the "
        + "metadata provider for them. Respects the dry run switch, so it can be read before it acts.";

    /// <inheritdoc />
    public string Key => "PosterOverlaysRepair";

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

        bool dryRun = plugin.Configuration.DryRun;
        var store = OverlayStateStore.Shared(plugin.DataFolderPath);
        var ids = store.KnownItemIds();

        int healthy = 0;
        int poisoned = 0;
        int repaired = 0;
        int noRemote = 0;
        int gone = 0;

        for (int i = 0; i < ids.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(i * 100.0 / Math.Max(1, ids.Count));

            string id = ids[i];
            var record = store.Get(id);
            if (record is null)
            {
                continue;
            }

            if (store.OriginalIsIntact(id, record))
            {
                healthy++;
                continue;
            }

            poisoned++;

            var item = _libraryManager.GetItemById(Guid.Parse(id));
            if (item is null)
            {
                store.Forget(id);
                gone++;
                continue;
            }

            if (dryRun)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "Poster overlays [dry run]: {Name} would get a fresh primary image from the provider - its "
                        + "cached original was overwritten.",
                        item.Name);
                }

                continue;
            }

            try
            {
                if (await RefetchPrimaryAsync(item, cancellationToken).ConfigureAwait(false))
                {
                    // Only after the fresh cover is on the item: the record and the poisoned
                    // file go away together, so a half-finished repair leaves the item findable
                    // rather than silently forgotten.
                    store.Forget(id);
                    repaired++;
                }
                else
                {
                    noRemote++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Poster overlays: repairing {Name} failed.", item.Name);
            }
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Poster overlays repair{Mode}: {Total} records - {Healthy} intact, {Poisoned} with an overwritten "
                + "cached original, of which {Repaired} got a fresh cover, {NoRemote} had no provider image to fetch "
                + "and were left alone, {Gone} no longer exist.",
                dryRun ? " [dry run]" : string.Empty,
                ids.Count,
                healthy,
                poisoned,
                repaired,
                noRemote,
                gone);
        }

        progress.Report(100);
    }

    private async Task<bool> RefetchPrimaryAsync(BaseItem item, CancellationToken cancellationToken)
    {
        // Hold the item so the watcher does not react to the upload half way through.
        using var hold = OverlayApplier.TryHold(item.Id);
        if (hold is null)
        {
            return false;
        }

        var query = new RemoteImageQuery(string.Empty)
        {
            ImageType = ImageType.Primary,
            IncludeAllLanguages = false,
            IncludeDisabledProviders = false,
        };

        var images = await _providerManager.GetAvailableRemoteImages(item, query, cancellationToken).ConfigureAwait(false);
        var best = images?.FirstOrDefault(i => i.Type == ImageType.Primary && !string.IsNullOrEmpty(i.Url));
        if (best?.Url is null)
        {
            _logger.LogWarning(
                "Poster overlays: no provider image found for {Name}. Its cached original stays marked as damaged, "
                + "so nothing is drawn on it and it will show up here again.",
                item.Name);
            return false;
        }

        await _providerManager.SaveImage(item, best.Url, ImageType.Primary, null, cancellationToken).ConfigureAwait(false);
        await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken).ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Poster overlays: fetched a fresh primary image for {Name}.", item.Name);
        }

        return true;
    }
}
