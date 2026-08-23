using System;
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
