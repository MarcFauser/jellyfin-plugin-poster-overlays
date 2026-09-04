using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.PosterOverlays.Badges;
using Jellyfin.Plugin.PosterOverlays.State;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PosterOverlays.Tasks;

/// <summary>
/// Throws away everything the plugin remembers and fetches fresh covers from the metadata
/// provider, so the upkeep loop can start from a known state.
/// </summary>
/// <remarks>
/// This exists because of a real failure in the first release, not as a precaution. The
/// scheduled task and the image-change watcher each kept their own state, so the watcher treated
/// an image the task had just badged as an untouched original, cached it, and badged it again.
/// The two badge stacks landed on the same pixels, so nothing looked wrong - until the corner
/// setting changed and the second badge appeared somewhere else on the poster.
/// <para>
/// The damage cannot be detected by inspection: a cache written that way is perfectly consistent
/// with its own record, it merely describes a badged image. An earlier version of this task
/// looked for inconsistent caches and reported "319 records, 319 intact" on a library where 319
/// were wrong. So the criterion is no longer consistency but scope: every item the plugin has a
/// record for, and every item it would badge, which together are exactly the set the broken run
/// worked on.
/// </para>
/// <para>
/// A badged image cannot be un-badged, so the only untouched cover left is the one the provider
/// still has. Only the primary image is replaced; nothing else about the item is touched.
/// </para>
/// </remarks>
public sealed class RepairOverlaysTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly ILocalizationManager _localization;
    private readonly ILogger<RepairOverlaysTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RepairOverlaysTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="providerManager">Instance of the <see cref="IProviderManager"/> interface.</param>
    /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
    public RepairOverlaysTask(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        ILocalizationManager localization,
        ILogger<RepairOverlaysTask> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _localization = localization;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Repair poster overlays";

    /// <inheritdoc />
    public string Description =>
        "Fetches a fresh primary image from the metadata provider for every item this plugin has "
        + "badged or would badge, and forgets what it remembered about them. Run this once if badges "
        + "were ever drawn twice. Respects the dry run switch.";

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
        var applier = new OverlayApplier(_providerManager, _libraryManager, _logger, plugin.Configuration, store, _localization);

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie },
            IsVirtualItem = false,
            Recursive = true,
        });

        int inScope = 0;
        int refetched = 0;
        int noRemote = 0;
        int failed = 0;

        for (int i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(i * 100.0 / Math.Max(1, items.Count));

            var item = items[i];
            var target = OverlayApplier.TargetOf(item);
            var built = BadgeBuilder.Build(
                item,
                plugin.Configuration,
                plugin.Configuration.CategoryFor(target),
                plugin.Configuration.PresetFor(target),
                null);
            if (!applier.NeedsRepair(item, built.Badges.Count))
            {
                continue;
            }

            inScope++;

            if (dryRun)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "Poster overlays [dry run]: {Name} would get a fresh primary image from the provider.",
                        item.Name);
                }

                continue;
            }

            try
            {
                if (await applier.RefetchFromProviderAsync(item, cancellationToken).ConfigureAwait(false))
                {
                    refetched++;
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
                failed++;
            }
        }

        store.Flush();

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Poster overlays repair{Mode}: {Total} items examined, {Scope} in scope - {Refetched} got a fresh "
                + "cover and were forgotten, {NoRemote} had no provider image and were left as they are, {Failed} "
                + "failed. Run \"Apply poster overlays\" afterwards to badge them once, cleanly.",
                dryRun ? " [dry run]" : string.Empty,
                items.Count,
                inScope,
                refetched,
                noRemote,
                failed);
        }

        progress.Report(100);
    }
}
