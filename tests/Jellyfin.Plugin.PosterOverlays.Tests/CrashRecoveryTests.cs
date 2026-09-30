using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PosterOverlays.Badges;
using Jellyfin.Plugin.PosterOverlays.Configuration;
using Jellyfin.Plugin.PosterOverlays.Rendering;
using Jellyfin.Plugin.PosterOverlays.State;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// What the upkeep loop does after the server died in the middle of it.
/// </summary>
/// <remarks>
/// Written after the reference server's state file was found empty on 2026-09-30. The empty file
/// itself is answered by <see cref="AtomicFile"/>; these are the two gaps that remain when every
/// file write is atomic: the moment between the upload and the record, and a record that is simply
/// gone while its cached original is not.
/// </remarks>
[Collection(BaseItemStaticsCollection.Name)]
public class CrashRecoveryTests : IDisposable
{
    private readonly string _folder;
    private readonly string _imagePath;
    private readonly byte[] _original = TestPosters.Textured(seed: 11, width: 400);

    public CrashRecoveryTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "poster-overlays-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _imagePath = Path.Combine(_folder, "poster.jpg");
        File.WriteAllBytes(_imagePath, _original);

        // A 4K stream, so the film gets exactly one badge and the badge set is not empty.
        BaseItem.MediaSourceManager = FakeMediaSourceManager.WithStreams(
            new MediaStream { Type = MediaStreamType.Video, Width = 3840, Height = 2160 });
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
    /// The regression, end to end: the upload lands, the server dies before the record is written,
    /// and the next run has to recognise the badged image as its own. Before the announcement the
    /// next run took it for a new cover - cached it as the original and drew on top.
    /// </summary>
    [Fact]
    public async Task AnUploadThatLandedBeforeTheCrashIsRecognisedAsOurs()
    {
        var movie = NewMovie();
        var store = new OverlayStateStore(Path.Combine(_folder, "data"));

        var dying = NewApplier(store, FakeProviderManager.DyingAfterTheUpload(out var fake), dryRun: false);
        await Assert.ThrowsAsync<IOException>(() => dying.ApplyAsync(movie, CancellationToken.None));
        Assert.Equal(1, fake.Uploads);

        byte[] onTheItem = File.ReadAllBytes(_imagePath);
        Assert.NotEqual(OverlayStateStore.Hash(_original), OverlayStateStore.Hash(onTheItem));

        // What the dead run left: a record that announces the image but never confirmed it.
        var left = new OverlayStateStore(Path.Combine(_folder, "data")).Get(OverlayApplier.Key(movie))!;
        Assert.Equal(OverlayStateStore.Hash(onTheItem), left.PendingBadgedHash);
        Assert.Equal(string.Empty, left.BadgedHash);

        // The next run, on a fresh store read from disk the way a restarted server reads it.
        var restarted = new OverlayStateStore(Path.Combine(_folder, "data"));
        var next = NewApplier(restarted, FakeProviderManager.NeverCalled(out var untouched), dryRun: false);
        var outcome = await next.ApplyAsync(movie, CancellationToken.None);

        Assert.Equal(OverlayOutcome.Unchanged, outcome);
        Assert.Equal(0, untouched.Uploads);

        var confirmed = restarted.Get(OverlayApplier.Key(movie))!;
        Assert.Equal(OverlayStateStore.Hash(onTheItem), confirmed.BadgedHash);
        Assert.Null(confirmed.PendingBadgedHash);

        // And the cached original is still the clean one, which is the whole point.
        Assert.Equal(OverlayStateStore.Hash(_original), OverlayStateStore.Hash(restarted.LoadOriginal(OverlayApplier.Key(movie), ".jpg")!));
    }

    /// <summary>
    /// The control that makes the test above mean something: the same state without the
    /// announcement - which is what the previous release left - is read as a new cover.
    /// </summary>
    [Fact]
    public async Task WithoutTheAnnouncementTheSameImageLooksLikeANewCover()
    {
        var movie = NewMovie();
        var store = new OverlayStateStore(Path.Combine(_folder, "data"));
        string id = OverlayApplier.Key(movie);
        byte[] badged = Draw(_original, "4K");
        File.WriteAllBytes(_imagePath, badged);
        store.SaveOriginal(id, _original, ".jpg");
        store.Set(id, new OverlayRecord { OriginalHash = OverlayStateStore.Hash(_original), OriginalExtension = ".jpg" });

        var outcome = await NewApplier(store, null!, dryRun: true).ApplyAsync(movie, CancellationToken.None);

        Assert.Equal(OverlayOutcome.CoverReplaced, outcome);
    }

    /// <summary>
    /// The other side of the announcement: the server died BEFORE the upload. The item still carries
    /// the old badged image, and the record's confirmed fields must still describe it - otherwise the
    /// announced badge set would be taken for the one on the item and the old image called correct.
    /// </summary>
    [Fact]
    public async Task ADeathBeforeTheUploadStillRedrawsTheOldImage()
    {
        var movie = NewMovie();
        string data = Path.Combine(_folder, "data");
        var store = new OverlayStateStore(data);
        string id = OverlayApplier.Key(movie);

        // The state a normal run leaves: an older badge set on the item, recorded as ours.
        byte[] oldBadged = Draw(_original, "HD");
        File.WriteAllBytes(_imagePath, oldBadged);
        store.SaveOriginal(id, _original, ".jpg");
        store.Set(id, new OverlayRecord
        {
            BadgeKey = BadgeBuilder.KeyOf([new BadgeSpec(BadgeCategory.Resolution, "HD")]),
            LookKey = OverlayApplier.LookKeyOf(BuiltInPresets.Get(BuiltInPresets.MovieId)!, 95),
            BadgedHash = OverlayStateStore.Hash(oldBadged),
            OriginalHash = OverlayStateStore.Hash(_original),
            OriginalExtension = ".jpg",
        });

        // Today the film is 4K, so the run redraws - and dies before the image reaches the item.
        var dying = NewApplier(store, FakeProviderManager.DyingBeforeTheUpload(out var fake), dryRun: false);
        await Assert.ThrowsAsync<IOException>(() => dying.ApplyAsync(movie, CancellationToken.None));
        Assert.Equal(1, fake.Uploads);
        Assert.Equal(OverlayStateStore.Hash(oldBadged), OverlayStateStore.Hash(File.ReadAllBytes(_imagePath)));

        // Read back as a restarted server would: the old image is still ours with the OLD badges,
        // so the next run redraws it. Announcing the new badges as if they were on the item would
        // have made this Unchanged - the old badges would have stayed on the cover for good.
        var restarted = new OverlayStateStore(data);
        var outcome = await NewApplier(restarted, null!, dryRun: true).ApplyAsync(movie, CancellationToken.None);

        Assert.Equal(OverlayOutcome.BadgesChanged, outcome);
    }

    /// <summary>
    /// A cached original with no record: the record was lost. Caching the image on the item now would
    /// overwrite the only clean copy with what is, here, a badged one. Run for real, not as a dry
    /// run, so "nothing was written" is a statement about the run and not about the dry-run switch.
    /// </summary>
    [Fact]
    public async Task ACachedOriginalWithoutARecordIsNeitherOverwrittenNorDrawnOn()
    {
        var movie = NewMovie();
        var store = new OverlayStateStore(Path.Combine(_folder, "data"));
        string id = OverlayApplier.Key(movie);
        store.SaveOriginal(id, _original, ".jpg");
        byte[] badged = Draw(_original, "4K");
        File.WriteAllBytes(_imagePath, badged);

        var applier = NewApplier(store, FakeProviderManager.NeverCalled(out var fake), dryRun: false);
        var outcome = await applier.ApplyAsync(movie, CancellationToken.None);

        Assert.Equal(OverlayOutcome.RecordMissing, outcome);
        Assert.Equal(0, fake.Uploads);
        Assert.Null(store.Get(id));
        Assert.Equal(OverlayStateStore.Hash(_original), OverlayStateStore.Hash(store.LoadOriginal(id, ".jpg")!));
        Assert.Equal(OverlayStateStore.Hash(badged), OverlayStateStore.Hash(File.ReadAllBytes(_imagePath)));
    }

    /// <summary>
    /// And the refusal does not fire where it would protect nothing: when the cached file IS the
    /// image on the item, caching it again changes nothing.
    /// </summary>
    [Fact]
    public async Task ACachedOriginalIdenticalToTheImageIsNoReasonToStop()
    {
        var movie = NewMovie();
        var store = new OverlayStateStore(Path.Combine(_folder, "data"));
        store.SaveOriginal(OverlayApplier.Key(movie), _original, ".jpg");

        var outcome = await NewApplier(store, null!, dryRun: true).ApplyAsync(movie, CancellationToken.None);

        Assert.Equal(OverlayOutcome.FirstRun, outcome);
    }

    private static byte[] Draw(byte[] original, string resolution) =>
        BadgeRenderer.Draw(
            original,
            [new BadgeSpec(BadgeCategory.Resolution, resolution)],
            BuiltInPresets.Get(BuiltInPresets.MovieId)!,
            95)!;

    private static PluginConfiguration NewConfig(bool dryRun)
    {
        var config = new PluginConfiguration { DryRun = dryRun };
        config.Movies.Enabled = true;
        config.Movies.AllowResolution = true;

        // Off: the audio label is the one badge that needs a library manager.
        config.Movies.AllowAudio = false;
        return config;
    }

    private static OverlayApplier NewApplier(OverlayStateStore store, IProviderManager providerManager, bool dryRun) =>
        new(providerManager, null!, NullLogger.Instance, NewConfig(dryRun), store);

    private Movie NewMovie() => new()
    {
        Id = Guid.NewGuid(),
        Name = "A film",
        Path = Path.Combine(_folder, "A.Film.2020.2160p.BluRay.x265-GRP", "film.mkv"),
        ImageInfos = [new ItemImageInfo { Type = ImageType.Primary, Path = _imagePath }],
    };
}
