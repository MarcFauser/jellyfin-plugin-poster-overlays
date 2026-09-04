using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PosterOverlays.Configuration;
using Jellyfin.Plugin.PosterOverlays.State;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// Taking a badge back off an item, driven through <see cref="OverlayApplier.ApplyAsync"/> the way
/// the nightly task drives it.
/// </summary>
/// <remarks>
/// This is the path that had never been covered, and it had never worked either. ApplyAsync takes
/// the static Busy lock for the item, and the restore branch inside it used to call the public
/// RestoreAsync - which tries to take the same lock again, fails, and returns false without
/// logging. The outcome surfaced as OriginalMissing, so a run reported a missing cache that was
/// sitting right there. Measured on the reference library on 2026-09-04: 222 items to restore,
/// 0 restored, 0 warnings.
/// <para>
/// The condition is what makes the test worth having: an item whose badge set has become empty
/// while the plugin's own image is still on it. That is rare enough that it took the audio badges
/// to produce it in numbers - which is why no earlier run ever exposed it.
/// </para>
/// </remarks>
public class RestoreThroughApplyTests : IDisposable
{
    private readonly string _folder;
    private readonly string _imagePath;
    private readonly byte[] _original = Encoding.UTF8.GetBytes("the untouched cover");
    private readonly byte[] _badged = Encoding.UTF8.GetBytes("the cover with a badge burnt in");

    public RestoreThroughApplyTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "poster-overlays-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _imagePath = Path.Combine(_folder, "poster.jpg");
        File.WriteAllBytes(_imagePath, _badged);

        // BaseItem.GetMediaStreams() goes through this static, and the badge builder calls it for
        // every video. Without it the item throws from inside Jellyfin before any plugin code runs.
        // No streams: an item with nothing a badge could describe is exactly the case under test.
        BaseItem.MediaSourceManager = FakeMediaSourceManager.WithStreams();
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
    /// The regression. Before the fix this returned OriginalMissing, because the restore branch
    /// asked for a lock its own caller was holding.
    /// </summary>
    [Fact]
    public async Task AnItemThatLostItsLastBadgeIsRestored()
    {
        var movie = NewMovie();
        var store = StoreThatKnows(movie);

        var applier = NewApplier(store);
        var outcome = await applier.ApplyAsync(movie, CancellationToken.None);

        Assert.Equal(OverlayOutcome.Restored, outcome);
    }

    /// <summary>
    /// The other half, and the reason the fix is one call and not the removal of the lock: the
    /// public wrapper must still refuse when somebody else is genuinely working on the item.
    /// Without this, "delete the lock" would also make the test above pass.
    /// </summary>
    [Fact]
    public async Task ThePublicWrapperStillRefusesWhileAnotherWorkerHoldsTheItem()
    {
        var movie = NewMovie();
        var store = StoreThatKnows(movie);
        var applier = NewApplier(store);

        using (var hold = OverlayApplier.TryHold(movie.Id))
        {
            Assert.NotNull(hold);
            Assert.False(await applier.RestoreAsync(movie, CancellationToken.None));
        }

        // And once the other worker is done, the very same call goes through.
        Assert.True(await applier.RestoreAsync(movie, CancellationToken.None));
    }

    /// <summary>
    /// The lock is released again afterwards, so a second pass over the same item is not silently
    /// skipped. A leaked lock would look exactly like "nothing to do".
    /// </summary>
    [Fact]
    public async Task TheLockIsFreeAgainAfterApply()
    {
        var movie = NewMovie();
        var applier = NewApplier(StoreThatKnows(movie));

        await applier.ApplyAsync(movie, CancellationToken.None);

        Assert.False(OverlayApplier.IsBusy(movie.Id));
    }

    private static PluginConfiguration NewConfig()
    {
        var config = new PluginConfiguration
        {
            // Nothing is written to the library from a test: the restore stops short of the upload
            // and reports what it would have done.
            DryRun = true,
        };
        config.Movies.Enabled = true;

        // Off on purpose. The audio label is the one badge that needs the library manager, and this
        // test constructs the applier without one.
        config.Movies.AllowAudio = false;
        return config;
    }

    private OverlayApplier NewApplier(OverlayStateStore store) =>
        new(null!, null!, NullLogger.Instance, NewConfig(), store);

    /// <summary>
    /// A film with an image and nothing a badge could describe - no path, no streams, no provider
    /// ids - so the badge set comes out empty.
    /// </summary>
    private Movie NewMovie() => new()
    {
        Id = Guid.NewGuid(),
        Name = "A film with nothing to say",
        ImageInfos = [new ItemImageInfo { Type = ImageType.Primary, Path = _imagePath }],
    };

    /// <summary>
    /// A store in the state the nightly run leaves behind: our badged image is on the item, the
    /// untouched original is cached, and the record ties the two together.
    /// </summary>
    private OverlayStateStore StoreThatKnows(Movie movie)
    {
        var store = new OverlayStateStore(_folder);
        string id = OverlayApplier.Key(movie);
        store.SaveOriginal(id, _original, ".jpg");
        store.Set(id, new OverlayRecord
        {
            BadgeKey = "1:AC3",
            OriginalHash = OverlayStateStore.Hash(_original),
            OriginalExtension = ".jpg",
            BadgedHash = OverlayStateStore.Hash(_badged),
        });
        return store;
    }
}
