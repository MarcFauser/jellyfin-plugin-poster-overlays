using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.PosterOverlays.Badges;
using Jellyfin.Plugin.PosterOverlays.State;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PosterOverlays.Tasks;

/// <summary>
/// Draws the badges and keeps them up to date.
/// </summary>
public sealed class ApplyOverlaysTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly ILogger<ApplyOverlaysTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplyOverlaysTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="providerManager">Instance of the <see cref="IProviderManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}"/> interface.</param>
    public ApplyOverlaysTask(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        ILogger<ApplyOverlaysTask> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Apply poster overlays";

    /// <inheritdoc />
    public string Key => "PosterOverlaysApply";

    /// <inheritdoc />
    public string Description =>
        "Draws the edition, resolution and video range badges onto the primary image, and puts them "
        + "back whenever a provider has replaced the cover.";

    /// <inheritdoc />
    public string Category => "Poster Overlays";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Once a day, at a time when nothing else is running. Everything else about the
        // schedule belongs in Jellyfin's own task screen, not in this plugin's settings.
        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.DailyTrigger,
                TimeOfDayTicks = TimeSpan.FromHours(3).Ticks,
            },
        };
    }

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
        if (!config.Enabled)
        {
            _logger.LogInformation("Poster overlays: disabled in the settings, nothing was done.");
            return;
        }

        var store = OverlayStateStore.Shared(plugin.DataFolderPath);
        var applier = new OverlayApplier(_providerManager, _libraryManager, _logger, config, store);

        var kinds = EnabledKinds(config);
        if (kinds.Length == 0)
        {
            _logger.LogInformation("Poster overlays: no category is switched on, nothing was done.");
            return;
        }

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = kinds,
            IsVirtualItem = false,
            Recursive = true,
        });

        var counts = new Dictionary<OverlayOutcome, int>();
        var groups = new Dictionary<string, List<GroupEntry>>(StringComparer.Ordinal);
        var unmapped = new Dictionary<string, int>(StringComparer.Ordinal);
        int done = 0;

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OverlayOutcome outcome;
            try
            {
                outcome = await applier.ApplyAsync(item, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One bad item must not end the run. The reason is logged with the item, which
                // is what makes it findable afterwards.
                _logger.LogError(ex, "Poster overlays: {Name} failed.", item.Name);
                outcome = OverlayOutcome.Failed;
            }

            counts[outcome] = counts.GetValueOrDefault(outcome) + 1;

            // Only for films. The report below asks "which entries share a TMDB id and end up
            // with identical badges", and for series that question is answered a level down -
            // the episodes are where two copies of the same thing sit side by side.
            if (item is MediaBrowser.Controller.Entities.Movies.Movie)
            {
                Collect(item, config, groups, unmapped);
            }

            done++;
            progress.Report(done * 100.0 / Math.Max(1, items.Count));
        }

        if (config.DryRun)
        {
            _logger.LogInformation(
                "Poster overlays: this was a DRY RUN. Nothing was uploaded, nothing was written, nothing was recorded.");
        }
        else
        {
            store.Flush();
        }

        Report(counts, groups, unmapped, items.Count);
        progress.Report(100);
    }

    /// <summary>
    /// The item kinds whose category is switched on.
    /// </summary>
    /// <remarks>
    /// Selected here rather than filtered inside the applier so a library where only films are
    /// badged is not walked over 25,000 episodes to say "skipped" 25,000 times.
    /// </remarks>
    private static BaseItemKind[] EnabledKinds(Configuration.PluginConfiguration config)
    {
        var kinds = new List<BaseItemKind>();

        if (config.Movies.Enabled)
        {
            kinds.Add(BaseItemKind.Movie);
        }

        if (config.Series.Enabled)
        {
            kinds.Add(BaseItemKind.Series);
        }

        if (config.Seasons.Enabled)
        {
            kinds.Add(BaseItemKind.Season);
        }

        if (config.Episodes.Enabled)
        {
            kinds.Add(BaseItemKind.Episode);
        }

        return kinds.ToArray();
    }

    private void Collect(
        BaseItem item,
        Configuration.PluginConfiguration config,
        Dictionary<string, List<GroupEntry>> groups,
        Dictionary<string, int> unmapped)
    {
        var target = OverlayApplier.TargetOf(item);
        var built = BadgeBuilder.Build(item, config, config.CategoryFor(target), config.PresetFor(target), null);

        string? candidate = FolderNameParser.UnmappedEditionCandidate(built.TagZone);
        if (candidate is not null)
        {
            unmapped[candidate] = unmapped.GetValueOrDefault(candidate) + 1;
        }

        if (item.ProviderIds is not null
            && item.ProviderIds.TryGetValue(MediaBrowser.Model.Entities.MetadataProvider.Tmdb.ToString(), out string? tmdb)
            && !string.IsNullOrEmpty(tmdb))
        {
            if (!groups.TryGetValue(tmdb, out var list))
            {
                list = new List<GroupEntry>();
                groups[tmdb] = list;
            }

            list.Add(new GroupEntry(item.Name ?? string.Empty, BadgeBuilder.KeyOf(built.Badges)));
        }
    }

    private void Report(
        Dictionary<OverlayOutcome, int> counts,
        Dictionary<string, List<GroupEntry>> groups,
        Dictionary<string, int> unmapped,
        int total)
    {
        if (!_logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        _logger.LogInformation(
            "Poster overlays: {Total} items - {First} badged for the first time, {Replaced} had a new cover from a "
            + "provider and were badged again, {Changed} redrawn because the badge set changed, {Look} redrawn "
            + "because the look changed, {Restored} restored, "
            + "{Unchanged} already correct, {NoImage} without an image, {Missing} skipped because the cached original "
            + "was gone, {Damaged} skipped because the cached original was overwritten, {Failed} failed.",
            total,
            counts.GetValueOrDefault(OverlayOutcome.FirstRun),
            counts.GetValueOrDefault(OverlayOutcome.CoverReplaced),
            counts.GetValueOrDefault(OverlayOutcome.BadgesChanged),
            counts.GetValueOrDefault(OverlayOutcome.LookChanged),
            counts.GetValueOrDefault(OverlayOutcome.Restored),
            counts.GetValueOrDefault(OverlayOutcome.Unchanged),
            counts.GetValueOrDefault(OverlayOutcome.NoImage),
            counts.GetValueOrDefault(OverlayOutcome.OriginalMissing),
            counts.GetValueOrDefault(OverlayOutcome.CacheInconsistent),
            counts.GetValueOrDefault(OverlayOutcome.Failed));

        int damaged = counts.GetValueOrDefault(OverlayOutcome.CacheInconsistent);
        if (damaged > 0)
        {
            _logger.LogWarning(
                "Poster overlays: {Damaged} items have a cached original that is no longer the image the record "
                + "describes, so nothing was drawn on them - another layer would not come off again. Run "
                + "\"Repair poster overlays\" to fetch a fresh cover from the provider for those.",
                damaged);
        }

        // The finding the badges cannot express, reported rather than hidden: several entries
        // share one film and end up with identical badges, so the tiles stay indistinguishable.
        // On the reference library that is 57 of 109 groups, and they differ only in codec and
        // audio - which is a job for a library cleanup, not for a poster.
        var ambiguous = groups
            .Where(g => g.Value.Count > 1 && g.Value.Select(e => e.BadgeKey).Distinct(StringComparer.Ordinal).Count() == 1)
            .ToList();

        if (ambiguous.Count > 0)
        {
            _logger.LogInformation(
                "Poster overlays: {Count} groups of entries share one film and get identical badges, so their tiles "
                + "still cannot be told apart. They differ in something no badge covers - codec, audio, or they are "
                + "duplicates that should not both exist.",
                ambiguous.Count);

            foreach (var group in ambiguous.Take(50))
            {
                _logger.LogInformation(
                    "Poster overlays: indistinguishable - TMDB {Tmdb}: {Names}",
                    group.Key,
                    string.Join(" | ", group.Value.Select(e => e.Name)));
            }

            if (ambiguous.Count > 50)
            {
                _logger.LogInformation(
                    "Poster overlays: {Rest} further indistinguishable groups were not listed.",
                    ambiguous.Count - 50);
            }
        }

        foreach (var pair in unmapped.OrderByDescending(p => p.Value).Take(25))
        {
            _logger.LogInformation(
                "Poster overlays: {Count}x an edition-looking phrase with no rule for it: \"{Candidate}\". "
                + "Worth adding to the catalogue if it really is a cut.",
                pair.Value.ToString(CultureInfo.InvariantCulture),
                pair.Key);
        }
    }

    private sealed record GroupEntry(string Name, string BadgeKey);
}
