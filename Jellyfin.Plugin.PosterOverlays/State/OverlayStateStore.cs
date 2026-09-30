using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
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

    private const string StateFileName = "state.json";
    private const string OriginalsFolderName = "originals";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private static readonly object SharedLock = new();

    /// <summary>
    /// The one store per data folder.
    /// </summary>
    /// <remarks>
    /// Keyed by folder rather than a single slot. The plugin only ever has one data folder, so in
    /// the server this holds one entry; keyed, a test that installs a rebuilt store for its own
    /// folder cannot leave it behind for the next test's folder.
    /// </remarks>
    private static readonly Dictionary<string, OverlayStateStore> SharedStores = new(StringComparer.Ordinal);

    private readonly object _gate = new();
    private readonly string _root;
    private readonly string _statePath;
    private readonly string _originalsPath;
    private readonly Dictionary<string, OverlayRecord> _records;

    /// <summary>
    /// False for a store that only answers questions - see <see cref="ForExamination"/>.
    /// </summary>
    private readonly bool _writable;

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
        : this(dataFolderPath, Load(Path.Combine(dataFolderPath, StateFileName)), writable: true)
    {
    }

    private OverlayStateStore(string dataFolderPath, Dictionary<string, OverlayRecord> records, bool writable)
    {
        _root = dataFolderPath;
        _statePath = Path.Combine(_root, StateFileName);
        _originalsPath = Path.Combine(_root, OriginalsFolderName);
        _records = records;
        _writable = writable;
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
            string key = Path.GetFullPath(dataFolderPath);
            if (!SharedStores.TryGetValue(key, out var store))
            {
                // Throws when the state file cannot be read, and deliberately leaves the slot
                // empty: every caller keeps getting the same refusal until the file is fixed or
                // InstallRebuilt puts a store here.
                store = new OverlayStateStore(dataFolderPath);
                SharedStores[key] = store;
            }

            return store;
        }
    }

    /// <summary>
    /// Gets the shared store, or says why there is none.
    /// </summary>
    /// <remarks>
    /// For the rebuild task, which is the one caller for whom an unreadable state file is the
    /// normal case rather than a failure.
    /// </remarks>
    /// <param name="dataFolderPath">The plugin's own data folder.</param>
    /// <param name="store">The store, or null when the state file cannot be read.</param>
    /// <param name="error">Why it cannot be read, or null.</param>
    /// <returns>True when the store is available.</returns>
    public static bool TryShared(string dataFolderPath, out OverlayStateStore? store, out InvalidOperationException? error)
    {
        try
        {
            store = Shared(dataFolderPath);
            error = null;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            store = null;
            error = ex;
            return false;
        }
    }

    /// <summary>
    /// Creates a store that knows no records and can never write.
    /// </summary>
    /// <remarks>
    /// The rebuild needs an applier to work out what an item's badges are, and an applier needs a
    /// store - but while the state file is unreadable there is no store to give it, and a writable
    /// empty one would be exactly the danger the refusal guards against: every item a first run,
    /// every badged cover an "original". This one answers "unknown" to every question and throws
    /// if anything tries to write through it.
    /// </remarks>
    /// <param name="dataFolderPath">The plugin's own data folder.</param>
    /// <returns>The read-only store.</returns>
    internal static OverlayStateStore ForExamination(string dataFolderPath) =>
        new(dataFolderPath, new Dictionary<string, OverlayRecord>(StringComparer.OrdinalIgnoreCase), writable: false);

    /// <summary>
    /// Replaces an unreadable state file with records worked out again, and makes the result the
    /// shared store.
    /// </summary>
    /// <remarks>
    /// Refuses unless the state file really is unreadable at the moment of the call. The rebuild
    /// task examines hundreds of items first, and a state that became readable in the meantime -
    /// somebody restored a backup - must not be overwritten by a reconstruction.
    /// <para>
    /// The unreadable file is moved aside, never deleted. It is evidence, and on the reference
    /// server it was also the only record of when things went wrong.
    /// </para>
    /// </remarks>
    /// <param name="dataFolderPath">The plugin's own data folder.</param>
    /// <param name="records">The reconstructed records.</param>
    /// <param name="stampUtc">Used in the name of the file moved aside.</param>
    /// <returns>The new shared store.</returns>
    public static OverlayStateStore InstallRebuilt(string dataFolderPath, IReadOnlyDictionary<string, OverlayRecord> records, DateTime stampUtc)
    {
        ArgumentNullException.ThrowIfNull(records);

        lock (SharedLock)
        {
            string key = Path.GetFullPath(dataFolderPath);
            if (SharedStores.ContainsKey(key))
            {
                throw new InvalidOperationException("The poster overlay state is readable; there is nothing to rebuild.");
            }

            string statePath = Path.Combine(dataFolderPath, StateFileName);
            if (File.Exists(statePath))
            {
                bool readable;
                try
                {
                    _ = Load(statePath);
                    readable = true;
                }
                catch (InvalidOperationException)
                {
                    readable = false;
                }

                if (readable)
                {
                    throw new InvalidOperationException(
                        "The poster overlay state file became readable while the rebuild was running. Nothing was replaced.");
                }

                string aside = statePath + ".unreadable-" + stampUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
                File.Move(statePath, aside);
            }

            var store = new OverlayStateStore(
                dataFolderPath,
                new Dictionary<string, OverlayRecord>(records, StringComparer.OrdinalIgnoreCase),
                writable: true);
            store.Flush();
            SharedStores[key] = store;
            return store;
        }
    }

    /// <summary>
    /// Lists every cached original in a data folder, grouped by item id.
    /// </summary>
    /// <remarks>
    /// Static because it has to work while no store can be built. File names that are not an item
    /// id followed by an extension are returned separately rather than skipped: this folder is
    /// written by nobody but this plugin, so an unexpected name is a finding.
    /// </remarks>
    /// <param name="dataFolderPath">The plugin's own data folder.</param>
    /// <returns>The cached originals and the names that did not fit.</returns>
    public static CachedOriginals ListCachedOriginals(string dataFolderPath)
    {
        var byId = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var unexpected = new List<string>();

        string folder = Path.Combine(dataFolderPath, OriginalsFolderName);
        if (Directory.Exists(folder))
        {
            foreach (string path in Directory.EnumerateFiles(folder))
            {
                string name = Path.GetFileName(path);
                if (name.EndsWith(AtomicFile.PendingSuffix, StringComparison.Ordinal))
                {
                    // Left over from a write that did not complete. Never an original.
                    continue;
                }

                string id = Path.GetFileNameWithoutExtension(name);
                if (!Guid.TryParseExact(id, "N", out _) || string.IsNullOrEmpty(Path.GetExtension(name)))
                {
                    unexpected.Add(name);
                    continue;
                }

                if (!byId.TryGetValue(id, out var paths))
                {
                    paths = new List<string>();
                    byId[id] = paths;
                }

                paths.Add(path);
            }
        }

        return new CachedOriginals(byId, unexpected);
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
    /// Writes several records and puts them on disk once.
    /// </summary>
    /// <remarks>
    /// For the rebuild, which adds hundreds at a time. <see cref="Set"/> rewrites the whole file per
    /// record, which is right for the upkeep loop and pointless here.
    /// </remarks>
    /// <param name="records">The records, by item id.</param>
    public void SetMany(IReadOnlyDictionary<string, OverlayRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        lock (_gate)
        {
            string now = DateTime.UtcNow.ToString(IsoUtc, CultureInfo.InvariantCulture);
            foreach (var pair in records)
            {
                pair.Value.UpdatedUtc = now;
                _records[pair.Key] = pair.Value;
            }

            FlushLocked();
        }
    }

    /// <summary>
    /// Forgets an item and deletes its cached original.
    /// </summary>
    /// <remarks>
    /// Every cached file of the item goes, not only the one the record names. A cover that was
    /// replaced by one in another format leaves the old file behind under the old extension, and
    /// an original with no record beside it is what the upkeep loop now refuses to draw over - so a
    /// forgotten item must not leave one.
    /// </remarks>
    /// <param name="itemId">The item id.</param>
    public void Forget(string itemId)
    {
        lock (_gate)
        {
            DeleteCachedOriginalsLocked(itemId, keep: null);
            _records.Remove(itemId);
            FlushLocked();
        }
    }

    /// <summary>
    /// Deletes the cached original of an item that has no record, without touching any record.
    /// </summary>
    /// <remarks>
    /// For the rebuild, when the item turned out to show its original anyway: the cached copy is
    /// then redundant, and left in place it would keep the upkeep loop from ever badging the item.
    /// </remarks>
    /// <param name="itemId">The item id.</param>
    /// <returns>
    /// False when the item has a record after all - it was badged in the meantime - and its cached
    /// original is therefore not redundant; nothing is deleted then.
    /// </returns>
    public bool DropCachedOriginal(string itemId)
    {
        lock (_gate)
        {
            if (_records.ContainsKey(itemId))
            {
                return false;
            }

            DeleteCachedOriginalsLocked(itemId, keep: null);
            return true;
        }
    }

    /// <summary>
    /// Finds the cached originals of an item, whatever their extension.
    /// </summary>
    /// <remarks>
    /// The question behind the refusal to overwrite: an item without a record should have no cached
    /// original at all. If it has one, the record was lost - and caching the image on the item now
    /// would replace the only unbadged copy with what may well be a badged one.
    /// </remarks>
    /// <param name="itemId">The item id.</param>
    /// <returns>The paths, possibly empty.</returns>
    public IReadOnlyList<string> CachedOriginalPaths(string itemId)
    {
        string fileName = Path.GetFileName(itemId);
        if (string.IsNullOrEmpty(fileName) || !Directory.Exists(_originalsPath))
        {
            return Array.Empty<string>();
        }

        var paths = new List<string>();
        foreach (string path in Directory.EnumerateFiles(_originalsPath, fileName + ".*"))
        {
            if (!path.EndsWith(AtomicFile.PendingSuffix, StringComparison.Ordinal)
                && string.Equals(Path.GetFileNameWithoutExtension(path), fileName, StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(path);
            }
        }

        return paths;
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
            EnsureWritable();
            Directory.CreateDirectory(_originalsPath);
            string path = OriginalPath(itemId, extension);

            // Atomic for the same reason as the state file: a torn original hashes to nothing the
            // record knows, and the item is stuck until somebody repairs it.
            AtomicFile.Write(path, bytes);

            // A cover in a different format than the last one leaves the old file under the old
            // extension. Removed here, after the new one is safely in place.
            DeleteCachedOriginalsLocked(itemId, keep: path);
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
        EnsureWritable();
        Directory.CreateDirectory(_root);

        // UTF-8 without a BOM: Jellyfin's own readers stumble over one, and there is no reason
        // to write a file that only this plugin can read back comfortably. SerializeToUtf8Bytes
        // writes none.
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(_records, SerializerOptions);

        // Atomic, not File.WriteAllText. The file is rewritten after every item, and a truncating
        // write left it empty on the reference server when the VM was killed mid-run - after which
        // the plugin, rightly, refused to do anything at all. See AtomicFile.
        AtomicFile.Write(_statePath, json);
    }

    private void EnsureWritable()
    {
        if (!_writable)
        {
            throw new InvalidOperationException(
                "This poster overlay store was opened for examination only and must not write.");
        }
    }

    private void DeleteCachedOriginalsLocked(string itemId, string? keep)
    {
        EnsureWritable();
        foreach (string path in CachedOriginalPaths(itemId))
        {
            if (keep is null || !string.Equals(path, keep, StringComparison.Ordinal))
            {
                File.Delete(path);
            }
        }
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
            // self-correcting.
            //
            // The advice in this message used to be "run Remove poster overlays, then delete the
            // file", and both halves were wrong. The removal task reads this same file and fails on
            // this same line, and with no records it would restore nothing anyway. Deleting the
            // file was the dangerous half: the next run would then cache every badged cover as its
            // original - overwriting the clean copy - and draw on top. Found on the reference server
            // on 2026-09-30, where the file was empty and 643 cached originals sat beside it.
            throw new InvalidOperationException(
                "The poster overlay state file at " + path + " could not be read. It was NOT "
                + "discarded: continuing without it would draw badges on top of badges. Do not "
                + "delete it - the cached originals beside it are the unbadged copies, and a run "
                + "without records would overwrite them. Run the \"Rebuild poster overlay state\" "
                + "task, with the dry run switched on first, to work the records out again from them.",
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

    /// <summary>
    /// The cached originals found in a data folder.
    /// </summary>
    /// <param name="ById">The files per item id, one or more each.</param>
    /// <param name="Unexpected">File names that are not an item id with an extension.</param>
    internal sealed record CachedOriginals(
        IReadOnlyDictionary<string, List<string>> ById,
        IReadOnlyList<string> Unexpected);
}
