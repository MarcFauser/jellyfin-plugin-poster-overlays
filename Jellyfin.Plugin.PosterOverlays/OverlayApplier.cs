using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PosterOverlays.Badges;
using Jellyfin.Plugin.PosterOverlays.Configuration;
using Jellyfin.Plugin.PosterOverlays.Rendering;
using Jellyfin.Plugin.PosterOverlays.State;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PosterOverlays;

/// <summary>
/// The upkeep loop for one item.
/// </summary>
/// <remarks>
/// The whole design rests on one decision: the badge is not supposed to survive. When a provider
/// delivers a new cover the badge is gone, and the plugin has to notice and put it back. The
/// image tag cannot detect that, because it also changes when this plugin uploads. The hash of
/// the image the plugin itself wrote can.
/// <list type="bullet">
/// <item>current image hashes to what we uploaded, badges unchanged - nothing to do;</item>
/// <item>hashes to ours but the badge set changed - redraw from the cached original;</item>
/// <item>hashes to something else - a provider replaced the cover, cache it and badge it;</item>
/// <item>unknown item - first run.</item>
/// </list>
/// Redrawing always starts from the cached original, never from the image on the item. That is
/// what keeps badges from stacking on badges, and stacking does not undo itself.
/// </remarks>
internal sealed class OverlayApplier
{
    /// <summary>
    /// Items this plugin is writing to right now.
    /// </summary>
    /// <remarks>
    /// Static, because the scheduled task and the image-change watcher are different objects
    /// working on the same library. Saving an image raises <c>ItemUpdated</c> while the applier
    /// is still between the upload and the record it is about to write, and in that window the
    /// watcher would look the item up, find nothing, and treat the image the applier had just
    /// uploaded as an untouched original. The shared store closes most of that window; this
    /// closes the rest.
    /// </remarks>
    private static readonly ConcurrentDictionary<Guid, byte> Busy = new();

    private readonly IProviderManager _providerManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger _logger;
    private readonly PluginConfiguration _config;
    private readonly OverlayStateStore _store;
    private readonly HashSet<string> _excluded;
    private readonly Dictionary<string, string> _editionOverrides;

    /// <summary>
    /// Episode numbers that occur more than once, per series, worked out once per series.
    /// </summary>
    /// <remarks>
    /// The alternative is one query per episode, which on the reference library is 25,419 of them
    /// against roughly 1,580 this way. The cache lives as long as the applier, which is one task
    /// run or one watcher event, so it cannot go stale across runs.
    /// </remarks>
    private readonly Dictionary<string, HashSet<string>> _twins = new(StringComparer.Ordinal);

    /// <summary>
    /// The other rows that share a presentation key, worked out once per key.
    /// </summary>
    /// <remarks>
    /// Same reasoning as <see cref="_twins"/>: the alternative is one query per item. The cache
    /// lives as long as the applier - one task run, or one watcher event - so it cannot go stale
    /// between runs.
    /// </remarks>
    private readonly Dictionary<string, IReadOnlyList<BaseItem>> _audioPeers = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="OverlayApplier"/> class.
    /// </summary>
    /// <param name="providerManager">Jellyfin's provider manager, used to write the image back.</param>
    /// <param name="libraryManager">Used to reach the episodes under a series or a season.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="config">The settings.</param>
    /// <param name="store">The state store.</param>
    public OverlayApplier(
        IProviderManager providerManager,
        ILibraryManager libraryManager,
        ILogger logger,
        PluginConfiguration config,
        OverlayStateStore store)
    {
        _providerManager = providerManager;
        _libraryManager = libraryManager;
        _logger = logger;
        _config = config;
        _store = store;
        _excluded = ParseIdList(config.ExcludedItemIds);
        _editionOverrides = ParseOverrides(config.EditionOverrides);
    }

    /// <summary>
    /// Brings one item up to date.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What happened.</returns>
    public async Task<OverlayOutcome> ApplyAsync(BaseItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!Busy.TryAdd(item.Id, 0))
        {
            // Somebody else is already working on this item. Waiting would only produce a
            // second identical answer.
            return OverlayOutcome.Skipped;
        }

        try
        {
            return await ApplyCoreAsync(item, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Busy.TryRemove(item.Id, out _);
        }
    }

    /// <summary>
    /// Says whether the plugin is currently writing to an item.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <returns>True while an apply or restore is in progress for it.</returns>
    public static bool IsBusy(Guid itemId) => Busy.ContainsKey(itemId);

    /// <summary>
    /// Claims an item so the watcher leaves it alone, for callers outside this class that write
    /// an image themselves - the repair task does.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <returns>A handle to release the claim, or null when somebody else already holds it.</returns>
    public static IDisposable? TryHold(Guid itemId)
    {
        return Busy.TryAdd(itemId, 0) ? new Hold(itemId) : null;
    }

    private async Task<OverlayOutcome> ApplyCoreAsync(BaseItem item, CancellationToken cancellationToken)
    {
        string id = Key(item);
        if (_excluded.Contains(id))
        {
            return OverlayOutcome.Skipped;
        }

        var target = TargetOf(item);
        var category = _config.CategoryFor(target);
        if (!category.Enabled)
        {
            return OverlayOutcome.Skipped;
        }

        var preset = _config.PresetFor(target);
        if (!_config.PresetReferenceIsIntact(target))
        {
            // Said out loud rather than absorbed. Drawing with settings nobody chose is the
            // failure mode that looks like success.
            _logger.LogWarning(
                "Poster overlays: the {Target} category points at a preset that does not exist, so the built-in "
                + "\"{Fallback}\" is being used. Pick a preset on the settings page.",
                target,
                preset.Name);
        }

        IReadOnlyList<BadgeSpec> badges;

        if (target is BadgeTarget.Series or BadgeTarget.Season)
        {
            // A series has no file of its own. What it may claim comes from its episodes, and the
            // episodes are fetched across the whole merge group - several database rows can share
            // one tile, and which of them supplies the image is Jellyfin's choice, not ours.
            badges = ChildAggregator.Aggregate(EpisodesUnder(item), _config, category, preset);
        }
        else
        {
            if (target == BadgeTarget.Episode
                && category.OnlyWhereItDisambiguates
                && !HasTwin(item))
            {
                return OverlayOutcome.Skipped;
            }

            _editionOverrides.TryGetValue(id, out string? editionOverride);
            var built = BadgeBuilder.Build(item, _config, category, preset, editionOverride, AudioLabelFor(item, category));
            badges = built.Badges;

            if (built.FolderClaimsHdr != built.StreamHasHdr && _logger.IsEnabled(LogLevel.Information))
            {
                // Reported, not resolved. The folder name and the stream disagree, and which one
                // is wrong is not this plugin's call - measured on the reference library, the name
                // misses 60 of 288 HDR titles and claims one that is not.
                _logger.LogInformation(
                    "Poster overlays: {Name} - the folder name says HDR/DV {Folder} but the video stream says {Stream}.",
                    item.Name,
                    built.FolderClaimsHdr,
                    built.StreamHasHdr);
            }
        }

        string badgeKey = BadgeBuilder.KeyOf(badges);

        string? currentPath = item.GetImagePath(ImageType.Primary, 0);
        if (string.IsNullOrEmpty(currentPath) || !File.Exists(currentPath))
        {
            return OverlayOutcome.NoImage;
        }

        byte[] current = await File.ReadAllBytesAsync(currentPath, cancellationToken).ConfigureAwait(false);
        string currentHash = OverlayStateStore.Hash(current);
        var record = _store.Get(id);

        // Before anything is decided: is the cached original still the image the record claims?
        // If not, something wrote over it, and on this plugin's own first release that something
        // was an already badged copy. Neither branch below is safe then - drawing would add a
        // layer, and restoring would hand back a badged image as though it were the original.
        // So nothing happens here and the repair task picks it up.
        if (record is not null && !_store.OriginalIsIntact(id, record))
        {
            return OverlayOutcome.CacheInconsistent;
        }

        bool oursOnTheItem = record is not null && string.Equals(currentHash, record.BadgedHash, StringComparison.Ordinal);

        if (badges.Count == 0)
        {
            if (!oursOnTheItem)
            {
                return OverlayOutcome.Unchanged;
            }

            return await RestoreAsync(item, cancellationToken).ConfigureAwait(false)
                ? OverlayOutcome.Restored
                : OverlayOutcome.OriginalMissing;
        }

        string lookKey = LookKeyOf(preset, _config.JpegQuality);
        bool sameBadges = oursOnTheItem && string.Equals(badgeKey, record!.BadgeKey, StringComparison.Ordinal);
        bool sameLook = oursOnTheItem && string.Equals(lookKey, record!.LookKey, StringComparison.Ordinal);

        if (sameBadges && sameLook)
        {
            return OverlayOutcome.Unchanged;
        }

        byte[] original;
        string extension;
        OverlayOutcome outcome;
        bool originalNeedsCaching = false;

        if (oursOnTheItem)
        {
            extension = record!.OriginalExtension;
            byte[]? cached = _store.LoadOriginal(id, extension);
            if (cached is null)
            {
                _logger.LogWarning(
                    "Poster overlays: {Name} needs new badges but its cached original is gone. Nothing was drawn - "
                    + "painting onto the badged image would stack a badge on a badge.",
                    item.Name);
                return OverlayOutcome.OriginalMissing;
            }

            original = cached;
            outcome = sameBadges ? OverlayOutcome.LookChanged : OverlayOutcome.BadgesChanged;
        }
        else
        {
            original = current;
            extension = Path.GetExtension(currentPath);
            if (string.IsNullOrEmpty(extension))
            {
                extension = ".jpg";
            }

            originalNeedsCaching = true;
            outcome = record is null ? OverlayOutcome.FirstRun : OverlayOutcome.CoverReplaced;
        }

        byte[]? badged = BadgeRenderer.Draw(original, badges, preset, _config.JpegQuality);
        if (badged is null)
        {
            _logger.LogWarning("Poster overlays: {Name} - the image could not be decoded.", item.Name);
            return OverlayOutcome.Failed;
        }

        if (_config.DryRun)
        {
            // Everything above was computed, including the drawing, so a dry run really does
            // exercise the decision it reports. Nothing below it touches the library or the
            // state, which is what makes the run repeatable.
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Poster overlays [dry run]: {Name} would be {Outcome} with {Badges}.",
                    item.Name,
                    outcome,
                    badgeKey.Length == 0 ? "no badges" : badgeKey);
            }

            return outcome;
        }

        if (originalNeedsCaching)
        {
            _store.SaveOriginal(id, original, extension);
        }

        string badgedHash = _config.WriteToMediaFolder
            ? await WriteBesideTheMediaAsync(item, badged, extension, cancellationToken).ConfigureAwait(false)
            : await UploadAsync(item, badged, extension, cancellationToken).ConfigureAwait(false);

        _store.Set(id, new OverlayRecord
        {
            BadgeKey = badgeKey,
            LookKey = lookKey,
            OriginalHash = OverlayStateStore.Hash(original),
            BadgedHash = badgedHash,
            OriginalExtension = extension,
        });

        return outcome;
    }

    /// <summary>
    /// Puts the cached original back on an item and forgets it.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True when an original was restored.</returns>
    public async Task<bool> RestoreAsync(BaseItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!Busy.TryAdd(item.Id, 0))
        {
            return false;
        }

        try
        {
            return await RestoreCoreAsync(item, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Busy.TryRemove(item.Id, out _);
        }
    }

    private async Task<bool> RestoreCoreAsync(BaseItem item, CancellationToken cancellationToken)
    {
        string id = Key(item);
        var record = _store.Get(id);
        if (record is null)
        {
            return false;
        }

        if (!_store.OriginalIsIntact(id, record))
        {
            _logger.LogWarning(
                "Poster overlays: {Name} was NOT restored - its cached original is not the image the record "
                + "describes, so putting it back would hand over a badged copy as an original. Run the repair task.",
                item.Name);
            return false;
        }

        byte[]? original = _store.LoadOriginal(id, record.OriginalExtension);
        if (original is null)
        {
            _logger.LogWarning(
                "Poster overlays: {Name} cannot be restored, its cached original is gone. The record is kept so the "
                + "next run does not mistake the badged image for an original.",
                item.Name);
            return false;
        }

        if (_config.DryRun)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Poster overlays [dry run]: {Name} would have its original restored.", item.Name);
            }

            return true;
        }

        await UploadAsync(item, original, record.OriginalExtension, cancellationToken).ConfigureAwait(false);
        _store.Forget(id);
        return true;
    }

    /// <summary>
    /// Throws away everything this plugin knows about an item and fetches a fresh primary image
    /// from the metadata provider.
    /// </summary>
    /// <remarks>
    /// The way back to a known state, and the only one there is. A badged image cannot be
    /// un-badged, so when the cached original is itself badged - which is what the first release
    /// left behind - neither the cache nor the item carries an untouched cover any more. The
    /// provider still does.
    /// <para>
    /// The order matters: the fresh cover goes on the item first, the record is dropped after.
    /// A failure in between leaves the item as it was, with its record, rather than badged and
    /// forgotten - which is the state in which the next run would cache a badged image as an
    /// original all over again.
    /// </para>
    /// </remarks>
    /// <param name="item">The item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True when a fresh image was fetched and the record dropped.</returns>
    public async Task<bool> RefetchFromProviderAsync(BaseItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        using var hold = TryHold(item.Id);
        if (hold is null)
        {
            return false;
        }

        // IncludeDisabledProviders = false is the load-bearing line here, not tidiness, and it
        // is worth saying so because the obvious assumption is wrong: passing the item does NOT
        // restrict the providers by itself. "Disabled" is defined relative to a library, so the
        // item only supplies the frame of reference - the switch is what applies it. Measured on
        // 10.11.11 by a neighbouring session: with the item present but the switch on, providers
        // that are not ticked for that library come back regardless.
        //
        // Jellyfin's own route does exactly that. RemoteImageController.GetRemoteImages builds
        // its query with IncludeDisabledProviders = true on purpose, because the image picker in
        // the web client is meant to offer everything. This plugin is not a picker: it downloads
        // without asking, so it must honour what the user configured for that library.
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
                "Poster overlays: no provider image found for {Name}. Nothing was changed, and its record is kept "
                + "so it shows up again on the next repair.",
                item.Name);
            return false;
        }

        await _providerManager.SaveImage(item, best.Url, ImageType.Primary, null, cancellationToken).ConfigureAwait(false);
        await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken).ConfigureAwait(false);
        _store.Forget(Key(item));

        return true;
    }

    /// <summary>
    /// Draws what this item would look like, without changing anything.
    /// </summary>
    /// <remarks>
    /// <b>Why this exists at all:</b> the settings page used to imitate the badges in SVG, which
    /// meant the drawing rules were written twice - once here in Skia and once in JavaScript - and
    /// two implementations of one thing drift apart. They already had: the centred corners had to
    /// be added in both, and the imitation measured its text as <c>length * fontSize * 0.62</c>
    /// where Skia measures it properly, so pill widths were never quite right. This renders through
    /// the real path instead, so the preview cannot be wrong about the plugin.
    /// <para>
    /// Nothing is written: no upload, no state record, no cached original. The applier is
    /// constructed with the unsaved configuration from the settings page, which is what makes a
    /// preview of an unsaved change possible in the first place.
    /// </para>
    /// <para>
    /// The refusals are deliberately the same ones the real run makes, and they are reported rather
    /// than worked around. An episode without a twin gets no badge under
    /// <see cref="CategorySettings.OnlyWhereItDisambiguates"/>, and a preview that quietly drew one
    /// anyway would be showing a picture the library will never contain.
    /// </para>
    /// </remarks>
    /// <param name="item">The item to draw.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rendered image and why it looks the way it does, or null when there is no image.</returns>
    internal async Task<PreviewResult?> RenderPreviewAsync(BaseItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        string id = Key(item);
        string? currentPath = item.GetImagePath(ImageType.Primary, 0);
        if (string.IsNullOrEmpty(currentPath) || !File.Exists(currentPath))
        {
            return null;
        }

        byte[] current = await File.ReadAllBytesAsync(currentPath, cancellationToken).ConfigureAwait(false);
        string extension = Path.GetExtension(currentPath);
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".jpg";
        }

        var record = _store.Get(id);

        // The image on the item is ours, so the unbadged picture is the cached one. Drawing onto
        // the badged copy would stack a badge on a badge - in a preview that is not destructive,
        // but it would be a lie about what the run produces.
        byte[] original = current;
        if (record is not null && string.Equals(OverlayStateStore.Hash(current), record.BadgedHash, StringComparison.Ordinal))
        {
            byte[]? cached = _store.LoadOriginal(id, record.OriginalExtension);
            if (cached is null)
            {
                return new PreviewResult(current, MimeType(extension), 0, "The cached original is gone, so this shows the image as it is now.");
            }

            original = cached;
            extension = record.OriginalExtension;
        }

        var target = TargetOf(item);
        var category = _config.CategoryFor(target);

        if (_excluded.Contains(id))
        {
            return new PreviewResult(original, MimeType(extension), 0, "This item is on the exception list, so it is left alone.");
        }

        if (!category.Enabled)
        {
            return new PreviewResult(original, MimeType(extension), 0, "The " + target + " category is switched off.");
        }

        var preset = _config.PresetFor(target);
        IReadOnlyList<BadgeSpec> badges;

        if (target is BadgeTarget.Series or BadgeTarget.Season)
        {
            badges = ChildAggregator.Aggregate(EpisodesUnder(item), _config, category, preset);
        }
        else
        {
            if (target == BadgeTarget.Episode && category.OnlyWhereItDisambiguates && !HasTwin(item))
            {
                return new PreviewResult(original, MimeType(extension), 0, "No second copy of this episode exists, and the category only badges where that tells two apart.");
            }

            _editionOverrides.TryGetValue(id, out string? editionOverride);
            badges = BadgeBuilder.Build(item, _config, category, preset, editionOverride, AudioLabelFor(item, category)).Badges;
        }

        if (badges.Count == 0)
        {
            return new PreviewResult(original, MimeType(extension), 0, "Nothing to say about this item that a badge could carry.");
        }

        byte[]? drawn = BadgeRenderer.Draw(original, badges, preset, _config.JpegQuality);
        if (drawn is null)
        {
            return new PreviewResult(original, MimeType(extension), 0, "The image could not be decoded.");
        }

        return new PreviewResult(drawn, "image/jpeg", badges.Count, string.Empty);
    }

    /// <summary>
    /// Says whether an item is one the repair has to touch.
    /// </summary>
    /// <remarks>
    /// Deliberately not "does its cached original look wrong" - that question cannot be answered.
    /// A cache written by the faulty release is perfectly consistent with its own record; it just
    /// describes a badged image. So the criterion is the one that can be checked: the plugin has
    /// a record for it, or it is an item the plugin would badge, which is exactly the set the
    /// broken run worked on.
    /// </remarks>
    /// <param name="item">The item.</param>
    /// <param name="badgeCount">How many badges the item would get now.</param>
    /// <returns>True when the item is in scope for a repair.</returns>
    public bool NeedsRepair(BaseItem item, int badgeCount)
    {
        ArgumentNullException.ThrowIfNull(item);
        return badgeCount > 0 || _store.Get(Key(item)) is not null;
    }

    /// <summary>
    /// Builds the id used as the state key. Matches the form Jellyfin puts in its own payloads.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>32 hex characters, no dashes.</returns>
    public static string Key(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Id.ToString("N", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Builds the key that says "drawn the same way as last time".
    /// </summary>
    /// <remarks>
    /// Only settings that change the pixels belong in here. Anything else - which items are
    /// excluded, whether the watcher is on, the dry run - must be left out, or every save of the
    /// settings page would order a full redraw of the whole library.
    /// <para>
    /// Invariant culture throughout: these numbers become a string that is compared against one
    /// written earlier, and on a German system "5,5" and "5.5" are the same setting with two
    /// spellings.
    /// </para>
    /// </remarks>
    /// <param name="preset">The look.</param>
    /// <param name="jpegQuality">The encoder quality, which is global rather than per preset.</param>
    /// <returns>The key.</returns>
    public static string LookKeyOf(BadgePreset preset, int jpegQuality)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var c = CultureInfo.InvariantCulture;
        string key = string.Join(
            '|',
            preset.Style.ToString(),
            preset.Corner.ToString(),
            preset.Direction.ToString(),
            preset.PillHeightPercent.ToString("R", c),
            preset.FontSizePercentOfPill.ToString("R", c),
            preset.PaddingPercentOfPill.ToString("R", c),
            preset.GapPercentOfPill.ToString("R", c),
            preset.CornerRadiusPercentOfPill.ToString("R", c),
            preset.BorderWidthPercentOfPill.ToString("R", c),
            preset.HorizontalMarginPercent.ToString("R", c),
            preset.VerticalMarginPercent.ToString("R", c),
            jpegQuality.ToString(c));

        // Appended only when they can reach the pixels. That is not a micro-optimisation: the
        // fields above are exactly the ones the flat configuration had, so a migrated movie
        // preset produces the key it produced before presets existed, and nothing is redrawn.
        // A redraw would start from the cached original, and some of those already carry a badge
        // from the faulty first release.
        if (preset.CompletenessColours)
        {
            key = string.Join(
                '|',
                key,
                preset.EffectiveUniformColour(),
                preset.EffectivePartialColour(),
                preset.PartialMarker.ToString(),
                preset.Glow ? preset.GlowRadiusPercentOfPill.ToString("R", c) : "noglow");
        }

        return key;
    }

    /// <summary>
    /// Which category's settings apply to an item.
    /// </summary>
    /// <remarks>
    /// Anything that is not one of the four falls to <see cref="BadgeTarget.Movie"/>, but that
    /// costs nothing: the applier only ever sees items the tasks selected, and they select by kind.
    /// </remarks>
    /// <param name="item">The item.</param>
    /// <returns>The target.</returns>
    public static BadgeTarget TargetOf(BaseItem item) => item switch
    {
        Episode => BadgeTarget.Episode,
        Season => BadgeTarget.Season,
        Series => BadgeTarget.Series,
        _ => BadgeTarget.Movie,
    };

    /// <summary>
    /// The episodes a series or a season is a statement about.
    /// </summary>
    /// <remarks>
    /// Fetched by the <b>presentation key</b>, not by parent id. A show split across resolution
    /// folders is several rows in the database that the client merges into one tile - measured on
    /// the reference library, one show is three Series rows and six Season rows - and the badge
    /// has to describe what that tile stands for, not what one of its rows happens to contain.
    /// <para>
    /// Rows without a file are dropped, and that is not tidiness. A library with the missing
    /// episode fetcher on carries thousands of placeholder rows; counting them would make almost
    /// every series "partial" for episodes that were never there to begin with.
    /// </para>
    /// </remarks>
    /// <param name="item">A series or a season.</param>
    /// <returns>The episodes, possibly empty.</returns>
    private IReadOnlyList<BaseItem> EpisodesUnder(BaseItem item)
    {
        string? key = SeriesKeyOf(item);
        if (string.IsNullOrEmpty(key))
        {
            return Array.Empty<BaseItem>();
        }

        int? season = item is Season s ? s.IndexNumber : null;
        return EpisodesForKey(key, season);
    }

    private List<BaseItem> EpisodesForKey(string key, int? seasonNumber)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Episode },
            IsVirtualItem = false,
            Recursive = true,
            SeriesPresentationUniqueKey = key,
        };

        if (seasonNumber is int number)
        {
            query.ParentIndexNumber = number;
        }

        return _libraryManager.GetItemList(query)
            .Where(e => !string.IsNullOrEmpty(e.Path))
            .ToList();
    }

    private string? SeriesKeyOf(BaseItem item)
    {
        switch (item)
        {
            case Series series:
                return series.PresentationUniqueKey;

            case Season season:
                var parent = season.Series ?? _libraryManager.GetItemById(season.SeriesId) as Series;
                return parent?.PresentationUniqueKey;

            case Episode episode:
                return episode.SeriesPresentationUniqueKey;

            default:
                return null;
        }
    }

    /// <summary>
    /// Whether another row carries the same episode of the same show.
    /// </summary>
    /// <remarks>
    /// The question behind "only badge an episode where it tells two copies apart". Answered once
    /// per series and remembered, because the alternative is a query per episode.
    /// </remarks>
    private bool HasTwin(BaseItem item)
    {
        if (item is not Episode episode
            || episode.ParentIndexNumber is not int season
            || episode.IndexNumber is not int number)
        {
            // Without numbers there is nothing to pair it with, so it cannot be a duplicate of
            // anything - and badging it would not disambiguate anything either.
            return false;
        }

        string? key = SeriesKeyOf(episode);
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        if (!_twins.TryGetValue(key, out var duplicated))
        {
            duplicated = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var sibling in EpisodesForKey(key, null))
            {
                if (sibling.ParentIndexNumber is not int s || sibling.IndexNumber is not int e)
                {
                    continue;
                }

                string slot = string.Create(CultureInfo.InvariantCulture, $"{s}:{e}");
                if (!seen.Add(slot))
                {
                    duplicated.Add(slot);
                }
            }

            _twins[key] = duplicated;
        }

        return duplicated.Contains(string.Create(CultureInfo.InvariantCulture, $"{season}:{number}"));
    }

    /// <summary>
    /// The audio label for an item, or null when drawing one would say nothing.
    /// </summary>
    /// <remarks>
    /// <b>This badge is only ever drawn where it separates two copies.</b> The reason is measured
    /// rather than aesthetic: of 105 groups on the reference library that share one film, 7 differ
    /// in nothing but the audio format. The other 2,300-odd films have no second copy at all, and
    /// a format badge on those is decoration - one nobody is reading the poster wall to find.
    /// <para>
    /// Two levels, in this order, because the label should carry exactly as much as the job needs.
    /// The format alone settles most of it; where two copies are both plain DTS and differ only in
    /// the channel layout - measured on two entries of Evangelion 2.0, 5.1 against 6.1 - the
    /// channels are added. Where even that is equal, nothing is drawn: four groups differ only in
    /// how many tracks they carry, and "two audio tracks" is not something a badge can usefully
    /// say.
    /// </para>
    /// <para>
    /// Grouped by Jellyfin's own <c>PresentationUniqueKey</c>, not by an id read out of
    /// ProviderIds. The presentation key is what decides which rows the client shows as one tile,
    /// and it follows an NFO that redefines the grouping; a key rebuilt from provider ids would
    /// stop agreeing with the client the moment somebody set a customid.
    /// </para>
    /// </remarks>
    /// <param name="item">The item.</param>
    /// <param name="category">Its category settings.</param>
    /// <returns>The label, or null.</returns>
    private string? AudioLabelFor(BaseItem item, CategorySettings category)
    {
        if (!category.AllowAudio)
        {
            return null;
        }

        string? key = item.PresentationUniqueKey;
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (!_audioPeers.TryGetValue(key, out var peers))
        {
            peers = _libraryManager.GetItemList(new InternalItemsQuery
            {
                PresentationUniqueKey = key,
                Recursive = true,
                IsVirtualItem = false,
            });

            _audioPeers[key] = peers;
        }

        // One copy has nothing to be told apart from.
        if (peers.Count < 2)
        {
            return null;
        }

        var languages = ParseLanguages(_config.AudioLanguages);

        foreach (bool withChannels in new[] { false, true })
        {
            string? mine = TechnicalBadges.Audio(TracksOf(item), withChannels, languages);
            if (mine is null)
            {
                return null;
            }

            foreach (var peer in peers)
            {
                if (peer.Id.Equals(item.Id))
                {
                    continue;
                }

                if (!string.Equals(mine, TechnicalBadges.Audio(TracksOf(peer), withChannels, languages), StringComparison.Ordinal))
                {
                    return mine;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<AudioTrack> TracksOf(BaseItem item)
    {
        var streams = item.GetMediaStreams();
        if (streams is null)
        {
            return Array.Empty<AudioTrack>();
        }

        var tracks = new List<AudioTrack>();
        foreach (var s in streams)
        {
            if (s.Type == MediaStreamType.Audio)
            {
                tracks.Add(new AudioTrack(s.Codec, s.Profile, s.Title, s.Channels, s.Language));
            }
        }

        return tracks;
    }

    private static string[] ParseLanguages(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string MimeType(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        _ => "image/jpeg",
    };

    private static HashSet<string> ParseIdList(string raw)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            set.Add(line.Replace("-", string.Empty, StringComparison.Ordinal));
        }

        return set;
    }

    private static Dictionary<string, string> ParseOverrides(string raw)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int sep = line.IndexOf('=', StringComparison.Ordinal);
            if (sep <= 0)
            {
                continue;
            }

            string id = line[..sep].Trim().Replace("-", string.Empty, StringComparison.Ordinal);
            map[id] = line[(sep + 1)..].Trim().ToUpperInvariant();
        }

        return map;
    }

    private async Task<string> UploadAsync(BaseItem item, byte[] bytes, string extension, CancellationToken cancellationToken)
    {
        using (var stream = new MemoryStream(bytes, writable: false))
        {
            await _providerManager
                .SaveImage(item, stream, MimeType(extension), ImageType.Primary, null, cancellationToken)
                .ConfigureAwait(false);
        }

        // SaveImage writes the file and updates the item in memory, but it does not persist -
        // measured in ImageSaver, which never calls UpdateToRepositoryAsync. Without this line
        // the new image exists on disk and no client ever asks for it.
        await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken).ConfigureAwait(false);

        // Hash what Jellyfin actually stored, not what was handed to it: the stored file is what
        // the next run will find on the item, so it is the only meaningful comparison basis.
        string? savedPath = item.GetImagePath(ImageType.Primary, 0);
        if (!string.IsNullOrEmpty(savedPath) && File.Exists(savedPath))
        {
            return OverlayStateStore.Hash(await File.ReadAllBytesAsync(savedPath, cancellationToken).ConfigureAwait(false));
        }

        return OverlayStateStore.Hash(bytes);
    }

    private async Task<string> WriteBesideTheMediaAsync(BaseItem item, byte[] bytes, string extension, CancellationToken cancellationToken)
    {
        string folder = item.ContainingFolderPath;
        string target = Path.Combine(folder, "poster" + extension);
        await File.WriteAllBytesAsync(target, bytes, cancellationToken).ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Poster overlays: wrote {Target}. Jellyfin picks a local image up on the next refresh of the item, "
                + "and will not replace it afterwards - which is the point of this setting and also its price.",
                target);
        }

        return OverlayStateStore.Hash(bytes);
    }

    /// <summary>
    /// Releases a claim taken with <see cref="TryHold"/>.
    /// </summary>
    private sealed class Hold : IDisposable
    {
        private readonly Guid _itemId;

        public Hold(Guid itemId)
        {
            _itemId = itemId;
        }

        public void Dispose()
        {
            Busy.TryRemove(_itemId, out _);
        }
    }
}
