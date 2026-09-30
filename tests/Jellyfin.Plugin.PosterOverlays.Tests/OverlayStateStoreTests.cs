using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Jellyfin.Plugin.PosterOverlays.State;
using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// The bookkeeping that decides whether a badge may be drawn.
/// </summary>
public class OverlayStateStoreTests : IDisposable
{
    private readonly string _folder;

    public OverlayStateStoreTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "poster-overlays-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>
    /// The failure this whole class was rewritten for. The scheduled task wrote its records only
    /// at the end of a run, so the image-change watcher - reacting to an upload the task had just
    /// made - read an empty file, decided it had never seen the item, and cached the badged image
    /// as the original. Writing on every Set is what makes a second reader see the truth.
    /// </summary>
    [Fact]
    public void ARecordIsOnDiskImmediately()
    {
        var writer = new OverlayStateStore(_folder);
        writer.Set("abc", new OverlayRecord { BadgeKey = "0:EXT", OriginalHash = "aa", BadgedHash = "bb" });

        var secondReader = new OverlayStateStore(_folder);

        Assert.NotNull(secondReader.Get("abc"));
        Assert.Equal("bb", secondReader.Get("abc")!.BadgedHash);
    }

    /// <summary>
    /// The timestamp is persisted, so it is ISO 8601 in UTC and must not depend on the culture
    /// the server happens to run under. 21 cultures use a full stop as their time separator, and
    /// a bare colon in a .NET format string means "the culture's separator", not a colon.
    /// </summary>
    [Fact]
    public void TheTimestampIsIsoUtcWhateverTheCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("da-DK");

            var store = new OverlayStateStore(_folder);
            store.Set("abc", new OverlayRecord());

            string written = store.Get("abc")!.UpdatedUtc;
            Assert.Matches(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$", written);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void AnIntactCacheIsRecognised()
    {
        var store = new OverlayStateStore(_folder);
        byte[] original = Encoding.UTF8.GetBytes("the untouched cover");
        store.SaveOriginal("abc", original, ".jpg");

        var record = new OverlayRecord
        {
            OriginalHash = OverlayStateStore.Hash(original),
            OriginalExtension = ".jpg",
        };

        Assert.True(store.OriginalIsIntact("abc", record));
    }

    /// <summary>
    /// And the case that matters: somebody wrote a different image over the cached original. A
    /// badged copy cannot be un-badged, so the caller has to stop rather than draw another layer.
    /// </summary>
    [Fact]
    public void AnOverwrittenCacheIsRecognised()
    {
        var store = new OverlayStateStore(_folder);
        byte[] original = Encoding.UTF8.GetBytes("the untouched cover");
        var record = new OverlayRecord
        {
            OriginalHash = OverlayStateStore.Hash(original),
            OriginalExtension = ".jpg",
        };

        store.SaveOriginal("abc", Encoding.UTF8.GetBytes("a copy that already carries a badge"), ".jpg");

        Assert.False(store.OriginalIsIntact("abc", record));
    }

    [Fact]
    public void AMissingCacheIsNotIntact()
    {
        var store = new OverlayStateStore(_folder);
        Assert.False(store.OriginalIsIntact("nothing-here", new OverlayRecord { OriginalHash = "aa" }));
    }

    [Fact]
    public void ForgettingRemovesTheRecordAndTheCachedFile()
    {
        var store = new OverlayStateStore(_folder);
        store.SaveOriginal("abc", new byte[] { 1, 2, 3 }, ".jpg");
        store.Set("abc", new OverlayRecord { OriginalExtension = ".jpg" });

        store.Forget("abc");

        Assert.Null(store.Get("abc"));
        Assert.Null(store.LoadOriginal("abc", ".jpg"));
        Assert.Null(new OverlayStateStore(_folder).Get("abc"));
    }

    /// <summary>
    /// The state the reference server was found in on 2026-09-30: the file there, and empty. The
    /// refusal is right; what changed is the advice. It used to say "run Remove poster overlays, then
    /// delete the file" - the first half cannot work, the removal task reads this same file, and the
    /// second half is the one that destroys the clean originals.
    /// </summary>
    [Fact]
    public void AnEmptyStateFileIsRefusedWithoutAdvisingItsDeletion()
    {
        File.WriteAllBytes(Path.Combine(_folder, "state.json"), []);

        var refused = Assert.Throws<InvalidOperationException>(() => new OverlayStateStore(_folder));

        Assert.Contains("Do not delete it", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Rebuild poster overlay state", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("delete the file", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove poster overlays", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The write goes through a sibling file and a rename, so a write that fails leaves the previous
    /// content as it was. A truncating write would have emptied the file first.
    /// </summary>
    [Fact]
    public void AWriteThatFailsLeavesThePreviousStateIntact()
    {
        var store = new OverlayStateStore(_folder);
        store.Set("abc", new OverlayRecord { BadgedHash = "before" });
        string statePath = Path.Combine(_folder, "state.json");
        byte[] before = File.ReadAllBytes(statePath);

        // A directory where the pending file has to go: the write fails at its first step.
        Directory.CreateDirectory(statePath + ".tmp");
        try
        {
            var failed = Record.Exception(() => store.Set("abc", new OverlayRecord { BadgedHash = "after" }));

            // Windows reports a directory in the way as access denied, Linux as an IOException.
            Assert.True(
                failed is IOException or UnauthorizedAccessException,
                "expected the write to fail, got " + (failed?.GetType().Name ?? "no exception"));
        }
        finally
        {
            Directory.Delete(statePath + ".tmp");
        }

        Assert.Equal(before, File.ReadAllBytes(statePath));
        Assert.Equal("before", new OverlayStateStore(_folder).Get("abc")!.BadgedHash);
    }

    [Fact]
    public void ASuccessfulWriteLeavesNoPendingFileBehind()
    {
        var store = new OverlayStateStore(_folder);
        store.Set("abc", new OverlayRecord());
        store.SaveOriginal("abc", new byte[] { 1, 2, 3 }, ".jpg");

        Assert.Empty(Directory.EnumerateFiles(_folder, "*.tmp", SearchOption.AllDirectories));
    }

    /// <summary>
    /// A pending file left over from a crash is not an original, for the lookup that guards the
    /// upkeep loop and for the listing the rebuild works from.
    /// </summary>
    [Fact]
    public void ALeftoverPendingFileIsNotAnOriginal()
    {
        string id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.Combine(_folder, "originals"));
        File.WriteAllBytes(Path.Combine(_folder, "originals", id + ".jpg.tmp"), new byte[] { 1 });

        Assert.Empty(new OverlayStateStore(_folder).CachedOriginalPaths(id));
        Assert.Empty(OverlayStateStore.ListCachedOriginals(_folder).ById);
    }

    /// <summary>
    /// A cover that came back in another format used to leave the old cached file behind. With the
    /// upkeep loop now refusing to cache over an original it has no record for, a stale file would
    /// block the item for good - so both saving and forgetting clear every extension.
    /// </summary>
    [Fact]
    public void AnOriginalInAnotherFormatReplacesTheOldOne()
    {
        var store = new OverlayStateStore(_folder);
        store.SaveOriginal("abc", new byte[] { 1 }, ".jpg");
        store.SaveOriginal("abc", new byte[] { 2 }, ".png");

        Assert.Equal(new[] { Path.Combine(_folder, "originals", "abc.png") }, store.CachedOriginalPaths("abc"));
    }

    [Fact]
    public void ForgettingRemovesCachedFilesOfEveryExtension()
    {
        var store = new OverlayStateStore(_folder);
        store.SaveOriginal("abc", new byte[] { 1 }, ".png");
        store.Set("abc", new OverlayRecord { OriginalExtension = ".png" });
        File.WriteAllBytes(Path.Combine(_folder, "originals", "abc.jpg"), new byte[] { 2 });

        store.Forget("abc");

        Assert.Empty(store.CachedOriginalPaths("abc"));
    }

    /// <summary>
    /// The store the rebuild hands its applier while the real one cannot be opened: it answers, and
    /// it must not be able to write - a writable empty store is the danger the refusal exists for.
    /// </summary>
    [Fact]
    public void AStoreOpenedForExaminationCannotWrite()
    {
        File.WriteAllBytes(Path.Combine(_folder, "state.json"), []);
        var store = OverlayStateStore.ForExamination(_folder);

        Assert.Null(store.Get("abc"));
        Assert.Throws<InvalidOperationException>(() => store.Set("abc", new OverlayRecord()));
        Assert.Throws<InvalidOperationException>(() => store.SaveOriginal("abc", new byte[] { 1 }, ".jpg"));
        Assert.Equal(0, new FileInfo(Path.Combine(_folder, "state.json")).Length);
    }

    [Fact]
    public void InstallingARebuiltStateRefusesWhileTheStateIsReadable()
    {
        new OverlayStateStore(_folder).Set("abc", new OverlayRecord { BadgedHash = "real" });

        Assert.Throws<InvalidOperationException>(() => OverlayStateStore.InstallRebuilt(
            _folder,
            new Dictionary<string, OverlayRecord> { ["xyz"] = new OverlayRecord() },
            DateTime.UtcNow));

        Assert.Equal("real", new OverlayStateStore(_folder).Get("abc")!.BadgedHash);
    }

    [Fact]
    public void ListingTheCacheNamesWhatDoesNotBelongThere()
    {
        string id = Guid.NewGuid().ToString("N");
        string originals = Path.Combine(_folder, "originals");
        Directory.CreateDirectory(originals);
        File.WriteAllBytes(Path.Combine(originals, id + ".jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(originals, "notes.txt"), new byte[] { 1 });

        var listed = OverlayStateStore.ListCachedOriginals(_folder);

        Assert.Equal(new[] { id }, listed.ById.Keys);
        Assert.Equal(new[] { "notes.txt" }, listed.Unexpected);
    }

    /// <summary>
    /// An id that tries to climb out of the originals folder must not be able to.
    /// </summary>
    [Fact]
    public void AnIdCannotEscapeTheOriginalsFolder()
    {
        var store = new OverlayStateStore(_folder);
        string outside = Path.Combine(_folder, "escaped.jpg");

        store.SaveOriginal(Path.Combine("..", "escaped"), new byte[] { 1 }, string.Empty);

        Assert.False(File.Exists(outside), "the id escaped into the parent folder");
    }
}
