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
    private readonly ILogger _logger;
    private readonly PluginConfiguration _config;
    private readonly OverlayStateStore _store;
    private readonly HashSet<string> _excluded;
    private readonly Dictionary<string, string> _editionOverrides;

    /// <summary>
    /// Initializes a new instance of the <see cref="OverlayApplier"/> class.
    /// </summary>
    /// <param name="providerManager">Jellyfin's provider manager, used to write the image back.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="config">The settings.</param>
    /// <param name="store">The state store.</param>
    public OverlayApplier(
        IProviderManager providerManager,
        ILogger logger,
        PluginConfiguration config,
        OverlayStateStore store)
    {
        _providerManager = providerManager;
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

        _editionOverrides.TryGetValue(id, out string? editionOverride);
        var built = BadgeBuilder.Build(item, _config, editionOverride);
        string badgeKey = BadgeBuilder.KeyOf(built.Badges);

        if (built.FolderClaimsHdr != built.StreamHasHdr && _logger.IsEnabled(LogLevel.Information))
        {
            // Reported, not resolved. The folder name and the stream disagree, and which one is
            // wrong is not this plugin's call - measured on the reference library, the name
            // misses 60 of 288 HDR titles and claims one that is not.
            _logger.LogInformation(
                "Poster overlays: {Name} - the folder name says HDR/DV {Folder} but the video stream says {Stream}.",
                item.Name,
                built.FolderClaimsHdr,
                built.StreamHasHdr);
        }

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

        if (built.Badges.Count == 0)
        {
            if (!oursOnTheItem)
            {
                return OverlayOutcome.Unchanged;
            }

            return await RestoreAsync(item, cancellationToken).ConfigureAwait(false)
                ? OverlayOutcome.Restored
                : OverlayOutcome.OriginalMissing;
        }

        string lookKey = LookKeyOf(_config);
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

        byte[]? badged = BadgeRenderer.Draw(original, built.Badges, _config);
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
    /// <param name="config">The settings.</param>
    /// <returns>The key.</returns>
    public static string LookKeyOf(PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var c = CultureInfo.InvariantCulture;
        return string.Join(
            '|',
            config.Style.ToString(),
            config.Corner.ToString(),
            config.Direction.ToString(),
            config.PillHeightPercent.ToString("R", c),
            config.FontSizePercentOfPill.ToString("R", c),
            config.PaddingPercentOfPill.ToString("R", c),
            config.GapPercentOfPill.ToString("R", c),
            config.CornerRadiusPercentOfPill.ToString("R", c),
            config.BorderWidthPercentOfPill.ToString("R", c),
            config.HorizontalMarginPercent.ToString("R", c),
            config.VerticalMarginPercent.ToString("R", c),
            config.JpegQuality.ToString(c));
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
