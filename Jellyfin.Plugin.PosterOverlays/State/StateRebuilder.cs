using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PosterOverlays.State;

/// <summary>
/// Works the records out again from the cached originals.
/// </summary>
/// <remarks>
/// <b>Why this exists.</b> On 2026-09-30 the reference server's <c>state.json</c> was found empty -
/// 0 bytes, left behind by a VM that was killed while the file was being rewritten - with 643
/// cached originals beside it. The plugin rightly refused to do anything without its records, and
/// the recovery its error message advised could not work: the removal task needs the very records
/// that were gone, and deleting the file would have let the next run cache every badged cover as
/// its "original".
/// <para>
/// The records can be recovered because a record is a claim that can be checked: "this cover is
/// that original with these badges". <see cref="OverlayApplier.ExamineForRebuildAsync"/> checks it
/// by drawing the badges again. What passes becomes a record; what shows its original anyway loses
/// its redundant cached copy; everything else is reported by name and left exactly as it is.
/// </para>
/// <para>
/// Split from the scheduled task that runs it, the way <see cref="OrphanSweep"/> is: the task needs
/// a live server, this only needs a folder, an applier and a way to look items up - so it can be
/// driven from a test, which is the only way to know its refusals can fire.
/// </para>
/// </remarks>
internal sealed class StateRebuilder
{
    private readonly string _dataFolderPath;
    private readonly Func<OverlayStateStore, OverlayApplier> _applierFor;
    private readonly Func<Guid, BaseItem?> _lookup;
    private readonly ILogger _logger;
    private readonly bool _dryRun;
    private readonly Func<DateTime> _utcNow;

    /// <summary>
    /// Initializes a new instance of the <see cref="StateRebuilder"/> class.
    /// </summary>
    /// <param name="dataFolderPath">The plugin's own data folder.</param>
    /// <param name="applierFor">Builds an applier over a given store.</param>
    /// <param name="lookup">Finds an item by id, or returns null.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="dryRun">When true, everything is worked out and nothing is written.</param>
    /// <param name="utcNow">The clock, used to name the unreadable file that is moved aside.</param>
    public StateRebuilder(
        string dataFolderPath,
        Func<OverlayStateStore, OverlayApplier> applierFor,
        Func<Guid, BaseItem?> lookup,
        ILogger logger,
        bool dryRun,
        Func<DateTime> utcNow)
    {
        _dataFolderPath = dataFolderPath;
        _applierFor = applierFor;
        _lookup = lookup;
        _logger = logger;
        _dryRun = dryRun;
        _utcNow = utcNow;
    }

    /// <summary>
    /// Examines every cached original and, outside a dry run, acts on what it found.
    /// </summary>
    /// <param name="progress">Progress, 0 to 100.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What was found.</returns>
    public async Task<RebuildReport> RunAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        bool readable = OverlayStateStore.TryShared(_dataFolderPath, out var live, out var unreadableBecause);
        var cached = OverlayStateStore.ListCachedOriginals(_dataFolderPath);
        var report = new RebuildReport(readable, _dryRun)
        {
            UnreadableBecause = unreadableBecause?.InnerException?.Message ?? unreadableBecause?.Message,
        };
        report.Unexpected.AddRange(cached.Unexpected);

        if (!readable && cached.ById.Count == 0)
        {
            // Nothing to rebuild from, and nothing to protect either - but also nothing that proves
            // the covers are clean. Installing an empty state here would let the next run take
            // whatever is on the items as originals, so the decision stays with the owner.
            report.Refused = "the state file is unreadable and there are no cached originals to rebuild it from. If "
                + "nothing was ever badged, the state file can be deleted; if badges were drawn, their originals are "
                + "gone and only \"Repair poster overlays\" can bring back clean covers.";
            return report;
        }

        // While the state is unreadable there is no store to hand the applier, and a writable empty
        // one would be the danger itself. The applier only needs a store to exist; it asks it nothing
        // during an examination.
        var applier = _applierFor(live ?? OverlayStateStore.ForExamination(_dataFolderPath));

        int done = 0;
        foreach (var pair in cached.ById)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(done++ * 90.0 / Math.Max(1, cached.ById.Count));

            string id = pair.Key;

            // Known first: an item with a record owns its cached original, whatever else lies next
            // to it - releases before 11.29 could leave a stale file under an older extension, and
            // that is no reason to call a healthy item ambiguous.
            if (live?.Get(id) is not null)
            {
                report.AlreadyKnown++;
                continue;
            }

            if (pair.Value.Count > 1)
            {
                report.Ambiguous.Add(id);
                continue;
            }

            var item = _lookup(Guid.ParseExact(id, "N"));
            if (item is null)
            {
                report.Gone.Add(id);
                continue;
            }

            RebuildVerdict verdict;
            using (var hold = OverlayApplier.TryHold(item.Id))
            {
                if (hold is null)
                {
                    report.Unmatched.Add(new Named(id, item.Name, "another worker is busy with it right now - run the rebuild again"));
                    continue;
                }

                try
                {
                    verdict = await applier.ExamineForRebuildAsync(item, pair.Value[0], cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // One unreadable file must not end the rebuild; it is named with its reason.
                    _logger.LogError(ex, "Poster overlays rebuild: examining {Name} failed.", item.Name);
                    report.Unmatched.Add(new Named(id, item.Name, "examining it failed: " + ex.Message));
                    continue;
                }
            }

            switch (verdict.Finding)
            {
                case RebuildFinding.Ours:
                    report.Rebuilt[id] = verdict.Record!;
                    if (verdict.ByteForByte)
                    {
                        report.RebuiltExactly++;
                    }

                    break;
                case RebuildFinding.ShowsOriginal:
                    report.ShowsOriginal.Add(new Named(id, item.Name, verdict.Reason));
                    break;
                case RebuildFinding.NoImage:
                    report.NoImage.Add(new Named(id, item.Name, verdict.Reason));
                    break;
                default:
                    report.Unmatched.Add(new Named(id, item.Name, verdict.Reason));
                    break;
            }
        }

        if (_dryRun)
        {
            progress.Report(100);
            return report;
        }

        OverlayStateStore store;
        if (readable)
        {
            store = live!;
            if (report.Rebuilt.Count > 0)
            {
                store.SetMany(report.Rebuilt);
            }
        }
        else
        {
            // Refuses if the file became readable in the meantime, and moves the unreadable one
            // aside rather than deleting it.
            store = OverlayStateStore.InstallRebuilt(_dataFolderPath, report.Rebuilt, _utcNow());
            report.Installed = true;
        }

        // Only now, with the records safely written: an item that shows its original needs no
        // cached copy, and one left behind without a record would keep the upkeep loop away from
        // the item for good.
        foreach (var entry in report.ShowsOriginal)
        {
            if (!store.DropCachedOriginal(entry.Id))
            {
                // Badged by the upkeep loop while the rebuild was examining - its record now owns
                // the cached original, which is exactly what should happen to it.
                report.ClaimedMeanwhile.Add(entry.Id);
            }
        }

        progress.Report(100);
        return report;
    }

    /// <summary>
    /// Writes the report to the log: one summary line, then every item that needs a look by name.
    /// </summary>
    /// <param name="report">The report.</param>
    public void Log(RebuildReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        string mode = report.DryRun ? " [dry run]" : string.Empty;

        if (report.Refused is not null)
        {
            _logger.LogWarning("Poster overlays rebuild{Mode}: nothing was done - {Reason}", mode, report.Refused);
            return;
        }

        if (report.UnreadableBecause is not null && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Poster overlays rebuild{Mode}: the state file could not be read ({Reason}), so every record comes "
                + "from the cached originals.",
                mode,
                report.UnreadableBecause);
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Poster overlays rebuild{Mode}: {Total} cached originals - {Rebuilt} records {Verb} ({Exact} matched byte "
                + "for byte, {Visual} as the same picture), {Original} items show their original anyway ({OriginalVerb}), "
                + "{Unmatched} could not be matched, {NoImage} have no image, {Gone} belong to items that no longer exist, "
                + "{Ambiguous} have more than one cached file, {Known} already had a record.",
                mode,
                report.Total,
                report.Rebuilt.Count,
                report.DryRun ? "would be rebuilt" : "rebuilt",
                report.RebuiltExactly,
                report.Rebuilt.Count - report.RebuiltExactly,
                report.ShowsOriginal.Count,
                report.DryRun ? "the cached copy would be dropped" : "the cached copy was dropped",
                report.Unmatched.Count,
                report.NoImage.Count,
                report.Gone.Count,
                report.Ambiguous.Count,
                report.AlreadyKnown);
        }

        // Every one by name, no cap: these are the items the owner has to look at, and a list cut
        // off at fifty reads as the whole list.
        foreach (var entry in report.Unmatched)
        {
            _logger.LogWarning(
                "Poster overlays rebuild{Mode}: {Name} ({Id}) was not matched - {Reason}. Nothing about it was changed, "
                + "and the upkeep loop leaves it alone. To start it over from a clean provider cover, call "
                + "POST /PosterOverlays/Repair/ with that id.",
                mode,
                entry.Name,
                entry.Id,
                entry.Reason);
        }

        foreach (string id in report.Ambiguous)
        {
            _logger.LogWarning(
                "Poster overlays rebuild{Mode}: {Id} has more than one cached original, so which one is the original "
                + "cannot be told. Left untouched.",
                mode,
                id);
        }

        foreach (string name in report.Unexpected)
        {
            _logger.LogWarning(
                "Poster overlays rebuild{Mode}: \"{Name}\" in the originals folder is not an item id with an extension. "
                + "This plugin does not write such names; left untouched.",
                mode,
                name);
        }

        if (report.Gone.Count > 0 && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Poster overlays rebuild{Mode}: the cached originals of {Count} items that no longer exist were left in "
                + "place. They are no use to the library, but an item recreated under a new id may still carry the "
                + "badged cover one of them belongs to.",
                mode,
                report.Gone.Count);
        }
    }

    /// <summary>
    /// An item in the report.
    /// </summary>
    /// <param name="Id">The state key.</param>
    /// <param name="Name">The item's name.</param>
    /// <param name="Reason">Why it is in this list.</param>
    internal sealed record Named(string Id, string Name, string Reason);

    /// <summary>
    /// What a rebuild found, and what it did.
    /// </summary>
    internal sealed class RebuildReport
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RebuildReport"/> class.
        /// </summary>
        /// <param name="stateWasReadable">Whether the state file could be read.</param>
        /// <param name="dryRun">Whether this was a dry run.</param>
        public RebuildReport(bool stateWasReadable, bool dryRun)
        {
            StateWasReadable = stateWasReadable;
            DryRun = dryRun;
        }

        /// <summary>Gets a value indicating whether the state file could be read.</summary>
        public bool StateWasReadable { get; }

        /// <summary>Gets a value indicating whether this was a dry run.</summary>
        public bool DryRun { get; }

        /// <summary>Gets or sets why the state file could not be read.</summary>
        public string? UnreadableBecause { get; set; }

        /// <summary>Gets or sets why nothing was done at all, or null.</summary>
        public string? Refused { get; set; }

        /// <summary>Gets or sets a value indicating whether a rebuilt state file replaced the unreadable one.</summary>
        public bool Installed { get; set; }

        /// <summary>Gets the reconstructed records, by item id.</summary>
        public Dictionary<string, OverlayRecord> Rebuilt { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Gets or sets how many of <see cref="Rebuilt"/> matched byte for byte.</summary>
        public int RebuiltExactly { get; set; }

        /// <summary>Gets the items that show their original anyway.</summary>
        public List<Named> ShowsOriginal { get; } = new();

        /// <summary>Gets the items whose cover could not be matched.</summary>
        public List<Named> Unmatched { get; } = new();

        /// <summary>Gets the items without an image.</summary>
        public List<Named> NoImage { get; } = new();

        /// <summary>Gets the ids of cached originals whose item no longer exists.</summary>
        public List<string> Gone { get; } = new();

        /// <summary>Gets the ids with more than one cached file.</summary>
        public List<string> Ambiguous { get; } = new();

        /// <summary>Gets the names in the originals folder that are not item ids.</summary>
        public List<string> Unexpected { get; } = new();

        /// <summary>Gets or sets how many cached originals already had a record.</summary>
        public int AlreadyKnown { get; set; }

        /// <summary>
        /// Gets the ids that showed their original during the examination and had a record by the
        /// time the cached copy was to be dropped, so it was kept.
        /// </summary>
        public List<string> ClaimedMeanwhile { get; } = new();

        /// <summary>Gets the number of item ids that were looked at.</summary>
        public int Total =>
            Rebuilt.Count + ShowsOriginal.Count + Unmatched.Count + NoImage.Count + Gone.Count + Ambiguous.Count + AlreadyKnown;
    }
}
