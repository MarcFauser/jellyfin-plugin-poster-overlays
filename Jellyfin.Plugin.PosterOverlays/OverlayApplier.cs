using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

        if (oursOnTheItem && string.Equals(badgeKey, record!.BadgeKey, StringComparison.Ordinal))
        {
            return OverlayOutcome.Unchanged;
        }

        byte[] original;
        string extension;
        OverlayOutcome outcome;

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
            outcome = OverlayOutcome.BadgesChanged;
        }
        else
        {
            original = current;
            extension = Path.GetExtension(currentPath);
            if (string.IsNullOrEmpty(extension))
            {
                extension = ".jpg";
            }

            _store.SaveOriginal(id, current, extension);
            outcome = record is null ? OverlayOutcome.FirstRun : OverlayOutcome.CoverReplaced;
        }

        byte[]? badged = BadgeRenderer.Draw(original, built.Badges, _config);
        if (badged is null)
        {
            _logger.LogWarning("Poster overlays: {Name} - the image could not be decoded.", item.Name);
            return OverlayOutcome.Failed;
        }

        string badgedHash = _config.WriteToMediaFolder
            ? await WriteBesideTheMediaAsync(item, badged, extension, cancellationToken).ConfigureAwait(false)
            : await UploadAsync(item, badged, extension, cancellationToken).ConfigureAwait(false);

        _store.Set(id, new OverlayRecord
        {
            BadgeKey = badgeKey,
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

        string id = Key(item);
        var record = _store.Get(id);
        if (record is null)
        {
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

        await UploadAsync(item, original, record.OriginalExtension, cancellationToken).ConfigureAwait(false);
        _store.Forget(id);
        return true;
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
}
