using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jellyfin.Plugin.PosterOverlays.State;

/// <summary>
/// Keeps the cached originals and the per-item records in the plugin's data folder.
/// </summary>
/// <remarks>
/// Two things live here: <c>state.json</c> with one record per item, and an <c>originals</c>
/// folder holding the untouched cover of every item that carries a badge. The originals are
/// what makes the upkeep loop safe - without them a second run would draw a badge on top of a
/// badge, which is the failure mode of every design that skips the cache.
/// </remarks>
internal sealed class OverlayStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _root;
    private readonly string _statePath;
    private readonly string _originalsPath;
    private readonly Dictionary<string, OverlayRecord> _records;

    /// <summary>
    /// Initializes a new instance of the <see cref="OverlayStateStore"/> class.
    /// </summary>
    /// <param name="dataFolderPath">The plugin's own data folder.</param>
    public OverlayStateStore(string dataFolderPath)
    {
        _root = dataFolderPath;
        _statePath = Path.Combine(_root, "state.json");
        _originalsPath = Path.Combine(_root, "originals");
        _records = Load(_statePath);
    }

    /// <summary>
    /// Gets the number of items currently under the plugin's care.
    /// </summary>
    public int Count => _records.Count;

    /// <summary>
    /// Gets the ids of every item the plugin has badged.
    /// </summary>
    /// <returns>The ids.</returns>
    public IReadOnlyCollection<string> KnownItemIds() => _records.Keys;

    /// <summary>
    /// Computes the hash used throughout the plugin.
    /// </summary>
    /// <param name="bytes">The image bytes.</param>
    /// <returns>The hash, lower case hex.</returns>
    public static string Hash(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    /// <summary>
    /// Reads the record of an item.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <returns>The record, or null when the item is unknown.</returns>
    public OverlayRecord? Get(string itemId)
    {
        return _records.TryGetValue(itemId, out var record) ? record : null;
    }

    /// <summary>
    /// Writes the record of an item.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="record">The record.</param>
    public void Set(string itemId, OverlayRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.UpdatedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        _records[itemId] = record;
    }

    /// <summary>
    /// Forgets an item and deletes its cached original.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    public void Forget(string itemId)
    {
        if (_records.TryGetValue(itemId, out var record))
        {
            string path = OriginalPath(itemId, record.OriginalExtension);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        _records.Remove(itemId);
    }

    /// <summary>
    /// Stores the untouched cover of an item.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="bytes">The image bytes.</param>
    /// <param name="extension">The file extension including the dot.</param>
    public void SaveOriginal(string itemId, byte[] bytes, string extension)
    {
        Directory.CreateDirectory(_originalsPath);
        File.WriteAllBytes(OriginalPath(itemId, extension), bytes);
    }

    /// <summary>
    /// Reads back the untouched cover of an item.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="extension">The file extension including the dot.</param>
    /// <returns>The bytes, or null when the cache no longer holds it.</returns>
    public byte[]? LoadOriginal(string itemId, string extension)
    {
        string path = OriginalPath(itemId, extension);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>
    /// Writes the records to disk.
    /// </summary>
    public void Flush()
    {
        Directory.CreateDirectory(_root);
        string json = JsonSerializer.Serialize(_records, SerializerOptions);

        // UTF-8 without a BOM: Jellyfin's own readers stumble over one, and there is no reason
        // to write a file that only this plugin can read back comfortably.
        File.WriteAllText(_statePath, json, new UTF8Encoding(false));
    }

    private static Dictionary<string, OverlayRecord> Load(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, OverlayRecord>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, OverlayRecord>>(File.ReadAllText(path));
            return loaded is null
                ? new Dictionary<string, OverlayRecord>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, OverlayRecord>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException ex)
        {
            // Deliberately fatal. Carrying on with an empty store would treat every item as a
            // first run, so the image currently on the item - the badged one - would be cached
            // as the "original" and the next badge would be drawn on top of it. Badges stacking
            // on badges is the one failure this cache exists to prevent, and it is not
            // self-correcting. The cached originals are untouched, so the honest recovery is to
            // run the removal task, which restores them, and then start again.
            throw new InvalidOperationException(
                "The poster overlay state file at " + path + " could not be read. It was NOT "
                + "discarded: continuing without it would draw badges on top of badges. Run the "
                + "\"Remove poster overlays\" task to restore the cached originals, then delete "
                + "the file and let the apply task run again.",
                ex);
        }
    }

    private string OriginalPath(string itemId, string extension)
    {
        return Path.Combine(_originalsPath, itemId + extension);
    }
}
