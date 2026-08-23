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
    /// <summary>
    /// ISO 8601, UTC, 24 hour. The separators are escaped rather than written bare: in a .NET
    /// custom format string a bare colon means "the current culture's time separator", and 21
    /// cultures - Danish and Assamese among them - use a full stop, which would persist
    /// 2026-08-23T14.05.07Z. The invariant culture below makes that correct anyway; the escaping
    /// keeps it correct if somebody ever drops the culture argument.
    /// </summary>
    private const string IsoUtc = "yyyy-MM-dd'T'HH':'mm':'ss'Z'";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private static readonly object SharedLock = new();
    private static OverlayStateStore? _shared;

    private readonly object _gate = new();
    private readonly string _root;
    private readonly string _statePath;
    private readonly string _originalsPath;
    private readonly Dictionary<string, OverlayRecord> _records;

    /// <summary>
    /// Initializes a new instance of the <see cref="OverlayStateStore"/> class.
    /// </summary>
    /// <remarks>
    /// Internal rather than private so a test can point one at its own folder. Everything in the
    /// plugin itself goes through <see cref="Shared"/> - two stores over one folder is the bug
    /// this class was rewritten for.
    /// </remarks>
    /// <param name="dataFolderPath">The plugin's own data folder.</param>
    internal OverlayStateStore(string dataFolderPath)
    {
        _root = dataFolderPath;
        _statePath = Path.Combine(_root, "state.json");
        _originalsPath = Path.Combine(_root, "originals");
        _records = Load(_statePath);
    }

    /// <summary>
    /// Gets the one store every part of the plugin uses.
    /// </summary>
    /// <remarks>
    /// A singleton, and this is not tidiness. The first release let the scheduled task and the
    /// image-change watcher each build their own store, and the task only wrote its records to
    /// disk when the whole run had finished. So the watcher, reacting to the upload the task
    /// had just made, read an empty file, concluded it had never seen the item, cached the
    /// freshly badged image as the "original" and drew a second badge on top of it. It happened
    /// to 417 of 439 items in one run. The two badges land on the same spot and are invisible,
    /// which is what made it dangerous rather than merely wrong.
    /// <para>
    /// One store, and a flush after every item, is what makes the "have I already done this"
    /// question answerable at all. Idempotence only protects when both sides read the same book.
    /// </para>
    /// </remarks>
    /// <param name="dataFolderPath">The plugin's own data folder.</param>
    /// <returns>The shared store.</returns>
    public static OverlayStateStore Shared(string dataFolderPath)
    {
        lock (SharedLock)
        {
            _shared ??= new OverlayStateStore(dataFolderPath);
            return _shared;
        }
    }

    /// <summary>
    /// Gets the ids of every item the plugin has badged.
    /// </summary>
    /// <returns>The ids, as a snapshot.</returns>
    public IReadOnlyList<string> KnownItemIds()
    {
        lock (_gate)
        {
            return new List<string>(_records.Keys);
        }
    }

    /// <summary>
    /// Counts the items currently under the plugin's care.
    /// </summary>
    /// <returns>The number of records.</returns>
    public int CountRecords()
    {
        lock (_gate)
        {
            return _records.Count;
        }
    }

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
        lock (_gate)
        {
            return _records.TryGetValue(itemId, out var record) ? record : null;
        }
    }

    /// <summary>
    /// Writes the record of an item and puts it on disk straight away.
    /// </summary>
    /// <remarks>
    /// The flush is part of writing, not a separate step a caller may forget. Deferring it to
    /// the end of a run is what let the watcher read an empty file for an item the task had
    /// just badged.
    /// </remarks>
    /// <param name="itemId">The item id.</param>
    /// <param name="record">The record.</param>
    public void Set(string itemId, OverlayRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_gate)
        {
            record.UpdatedUtc = DateTime.UtcNow.ToString(IsoUtc, CultureInfo.InvariantCulture);
            _records[itemId] = record;
            FlushLocked();
        }
    }

    /// <summary>
    /// Forgets an item and deletes its cached original.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    public void Forget(string itemId)
    {
        lock (_gate)
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
            FlushLocked();
        }
    }

    /// <summary>
    /// Says whether the cached original of an item is still the image the record describes.
    /// </summary>
    /// <remarks>
    /// The one check that finds a poisoned cache. If the file no longer hashes to the recorded
    /// original, something wrote a different image over it - in the failure this plugin shipped
    /// with, that was an already badged copy. Drawing on top of it would add another layer, and
    /// layers do not come off, so the caller has to stop rather than carry on.
    /// </remarks>
    /// <param name="itemId">The item id.</param>
    /// <param name="record">The record.</param>
    /// <returns>True when the cached file matches the recorded hash.</returns>
    public bool OriginalIsIntact(string itemId, OverlayRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        byte[]? cached = LoadOriginal(itemId, record.OriginalExtension);
        if (cached is null)
        {
            return false;
        }

        return string.Equals(Hash(cached), record.OriginalHash, StringComparison.Ordinal);
    }

    /// <summary>
    /// Stores the untouched cover of an item.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="bytes">The image bytes.</param>
    /// <param name="extension">The file extension including the dot.</param>
    public void SaveOriginal(string itemId, byte[] bytes, string extension)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_originalsPath);
            File.WriteAllBytes(OriginalPath(itemId, extension), bytes);
        }
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
    /// Writes the records to disk. Normally unnecessary - <see cref="Set"/> and
    /// <see cref="Forget"/> already do it - but harmless at the end of a run.
    /// </summary>
    public void Flush()
    {
        lock (_gate)
        {
            FlushLocked();
        }
    }

    private void FlushLocked()
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

    /// <summary>
    /// Builds the path of a cached original.
    /// </summary>
    /// <remarks>
    /// The id is passed through <see cref="Path.GetFileName(string)"/> before it becomes part of
    /// a path. An item id is a GUID and cannot contain a separator, so in practice this changes
    /// nothing - but the id reaches this method from an HTTP route, and a guard that only holds
    /// because of an invariant somewhere else is not a guard. This one strips any directory part
    /// whatever the caller hands over.
    /// </remarks>
    private string OriginalPath(string itemId, string extension)
    {
        string fileName = Path.GetFileName(itemId + extension);
        if (string.IsNullOrEmpty(fileName))
        {
            throw new ArgumentException("The item id does not produce a usable file name.", nameof(itemId));
        }

        return Path.Combine(_originalsPath, fileName);
    }
}
