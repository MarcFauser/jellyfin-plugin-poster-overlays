using System;
using System.Collections.Generic;
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
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// Working the records out again from the cached originals, the way the reference server needed it
/// on 2026-09-30: an empty state file beside 643 clean originals.
/// </summary>
/// <remarks>
/// One library, one of each kind of item the rebuild has to tell apart, so every test sees all of
/// them at once - a verdict that is right on its own and wrong next to the others would not show up
/// with one item per test.
/// </remarks>
[Collection(BaseItemStaticsCollection.Name)]
public class StateRebuilderTests : IDisposable
{
    private static readonly DateTime Stamp = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _root;
    private readonly string _data;
    private readonly Dictionary<Guid, BaseItem> _library = new();
    private readonly Dictionary<string, Movie> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _originals = new(StringComparer.Ordinal);

    public StateRebuilderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "poster-overlays-rebuild-" + Guid.NewGuid().ToString("N"));
        _data = Path.Combine(_root, "data");
        Directory.CreateDirectory(_data);

        BaseItem.MediaSourceManager = FakeMediaSourceManager.WithStreams(
            new MediaStream { Type = MediaStreamType.Video, Width = 3840, Height = 2160 });

        // Each film: its cached original, and what the cover on the item is now.
        Add("exact", seed: 21, cover: o => Draw(o, "4K"));
        Add("reencoded", seed: 22, cover: o => TestPosters.Reencode(Draw(o, "4K"), 95));
        Add("original", seed: 23, cover: o => o);
        Add("newcover", seed: 24, cover: _ => TestPosters.Textured(seed: 99, width: 400));
        Add("otherbadge", seed: 25, cover: o => Draw(o, "8K"));
        Add("gone", seed: 26, cover: o => Draw(o, "4K"), inLibrary: false);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task EachKindOfItemGetsItsOwnVerdict()
    {
        MakeStateUnreadable();

        var report = await Rebuild(dryRun: true);

        Assert.False(report.StateWasReadable);
        Assert.Equal(new[] { Id("exact"), Id("reencoded") }.Order(), report.Rebuilt.Keys.Order());
        Assert.Equal(1, report.RebuiltExactly);
        Assert.Equal(new[] { Id("original") }, report.ShowsOriginal.Select(n => n.Id));
        Assert.Equal(new[] { Id("newcover"), Id("otherbadge") }.Order(), report.Unmatched.Select(n => n.Id).Order());
        Assert.Equal(new[] { Id("gone") }, report.Gone);
        Assert.Equal(6, report.Total);

        // The record says what the NEXT run will find on the item: the cover as it is, not the redraw.
        var reencoded = report.Rebuilt[Id("reencoded")];
        Assert.Equal(OverlayStateStore.Hash(File.ReadAllBytes(CoverPath("reencoded"))), reencoded.BadgedHash);
        Assert.Equal(OverlayStateStore.Hash(_originals["reencoded"]), reencoded.OriginalHash);
    }

    /// <summary>
    /// A dry run changes nothing at all - the empty state file included, which is what the owner
    /// runs it to decide about.
    /// </summary>
    [Fact]
    public async Task ADryRunChangesNothing()
    {
        MakeStateUnreadable();
        var before = Snapshot();

        await Rebuild(dryRun: true);

        Assert.Equal(before, Snapshot());
        Assert.False(OverlayStateStore.TryShared(_data, out _, out _));
    }

    [Fact]
    public async Task ARealRunRebuildsWhatItProvedAndTouchesNothingElse()
    {
        MakeStateUnreadable();

        var report = await Rebuild(dryRun: false);

        Assert.True(report.Installed);

        // The unreadable file is kept as evidence, under a name that says when.
        string aside = Path.Combine(_data, "state.json.unreadable-20260930T120000Z");
        Assert.True(File.Exists(aside));
        Assert.Equal(0, new FileInfo(aside).Length);

        // The new state is the shared one and holds exactly the proven records.
        Assert.True(OverlayStateStore.TryShared(_data, out var store, out _));
        Assert.Equal(new[] { Id("exact"), Id("reencoded") }.Order(), store!.KnownItemIds().Order());

        // An item that shows its original lost its redundant cached copy; everybody else kept theirs.
        Assert.Empty(store.CachedOriginalPaths(Id("original")));
        foreach (string name in new[] { "exact", "reencoded", "newcover", "otherbadge", "gone" })
        {
            Assert.Single(store.CachedOriginalPaths(Id(name)));
        }

        // No cover was changed by the rebuild.
        foreach (var movie in _byName.Values)
        {
            Assert.True(File.Exists(movie.GetImagePath(ImageType.Primary, 0)));
        }
    }

    /// <summary>
    /// The point of the whole exercise, checked through the upkeep loop itself: a rebuilt item is
    /// recognised as done, and an unmatched one is left alone rather than badged on top.
    /// </summary>
    [Fact]
    public async Task AfterTheRebuildTheUpkeepLoopTrustsWhatWasProvenAndOnlyThat()
    {
        MakeStateUnreadable();
        await Rebuild(dryRun: false);
        Assert.True(OverlayStateStore.TryShared(_data, out var store, out _));

        var applier = new OverlayApplier(null!, null!, NullLogger.Instance, Config(dryRun: true), store!);

        Assert.Equal(OverlayOutcome.Unchanged, await applier.ApplyAsync(_byName["exact"], CancellationToken.None));
        Assert.Equal(OverlayOutcome.Unchanged, await applier.ApplyAsync(_byName["reencoded"], CancellationToken.None));
        Assert.Equal(OverlayOutcome.RecordMissing, await applier.ApplyAsync(_byName["newcover"], CancellationToken.None));
        Assert.Equal(OverlayOutcome.RecordMissing, await applier.ApplyAsync(_byName["otherbadge"], CancellationToken.None));

        // The cached copy of this one was dropped, so it is an ordinary first run now.
        Assert.Equal(OverlayOutcome.FirstRun, await applier.ApplyAsync(_byName["original"], CancellationToken.None));
    }

    /// <summary>
    /// With a readable state the rebuild only fills in what is missing, and leaves existing records
    /// exactly as they were.
    /// </summary>
    [Fact]
    public async Task AReadableStateOnlyGetsTheMissingRecords()
    {
        var known = new OverlayRecord { BadgeKey = "kept as it was", OriginalExtension = ".jpg" };
        var seed = new OverlayStateStore(_data);
        seed.Set(Id("exact"), known);

        var report = await Rebuild(dryRun: false);

        Assert.True(report.StateWasReadable);
        Assert.False(report.Installed);
        Assert.Equal(1, report.AlreadyKnown);
        Assert.Equal(new[] { Id("reencoded") }, report.Rebuilt.Keys);

        var reread = new OverlayStateStore(_data);
        Assert.Equal("kept as it was", reread.Get(Id("exact"))!.BadgeKey);
        Assert.NotNull(reread.Get(Id("reencoded")));
    }

    /// <summary>
    /// With nothing to rebuild from there is also nothing that proves the covers clean, so it does
    /// not install an empty state that would let the next run take badged covers for originals.
    /// </summary>
    [Fact]
    public async Task AnUnreadableStateWithoutOriginalsIsRefused()
    {
        Directory.Delete(Path.Combine(_data, "originals"), recursive: true);
        MakeStateUnreadable();

        var report = await Rebuild(dryRun: false);

        Assert.NotNull(report.Refused);
        Assert.False(report.Installed);
        Assert.Equal(0, new FileInfo(Path.Combine(_data, "state.json")).Length);
    }

    private static byte[] Draw(byte[] original, string resolution) =>
        BadgeRenderer.Draw(
            original,
            [new BadgeSpec(BadgeCategory.Resolution, resolution)],
            BuiltInPresets.Get(BuiltInPresets.MovieId)!,
            95)!;

    private static PluginConfiguration Config(bool dryRun)
    {
        var config = new PluginConfiguration { DryRun = dryRun };
        config.Movies.Enabled = true;
        config.Movies.AllowResolution = true;
        config.Movies.AllowAudio = false;
        return config;
    }

    private Task<StateRebuilder.RebuildReport> Rebuild(bool dryRun)
    {
        var rebuilder = new StateRebuilder(
            _data,
            store => new OverlayApplier(null!, null!, NullLogger.Instance, Config(dryRun), store),
            id => _library.GetValueOrDefault(id),
            NullLogger.Instance,
            dryRun,
            () => Stamp);
        return rebuilder.RunAsync(new Progress<double>(), CancellationToken.None);
    }

    /// <summary>The state the reference server was in: the file there, and empty.</summary>
    private void MakeStateUnreadable() => File.WriteAllBytes(Path.Combine(_data, "state.json"), []);

    private void Add(string name, int seed, Func<byte[], byte[]> cover, bool inLibrary = true)
    {
        byte[] original = TestPosters.Textured(seed: seed, width: 400);
        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = Path.Combine(_root, "films", name + ".2020.2160p.BluRay.x265-GRP", "film.mkv"),
            ImageInfos = [new ItemImageInfo { Type = ImageType.Primary, Path = CoverPath(name) }],
        };

        Directory.CreateDirectory(Path.GetDirectoryName(CoverPath(name))!);
        File.WriteAllBytes(CoverPath(name), cover(original));

        string originals = Path.Combine(_data, "originals");
        Directory.CreateDirectory(originals);
        File.WriteAllBytes(Path.Combine(originals, OverlayApplier.Key(movie) + ".jpg"), original);

        _byName[name] = movie;
        _originals[name] = original;
        if (inLibrary)
        {
            _library[movie.Id] = movie;
        }
    }

    private string CoverPath(string name) => Path.Combine(_root, "covers", name + ".jpg");

    private string Id(string name) => OverlayApplier.Key(_byName[name]);

    /// <summary>Every file under the test folder with its hash - "nothing changed", measured.</summary>
    private string Snapshot() => string.Join(
        '\n',
        Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(p => p + " " + OverlayStateStore.Hash(File.ReadAllBytes(p))));
}
