using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.PosterOverlays.Configuration;
using Jellyfin.Plugin.PosterOverlays.State;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// What a run tells its owner: the message about a cover that cannot be decoded, and the tally of
/// folder names that disagree with their video stream.
/// </summary>
/// <remarks>
/// Both were changed because of what a real run looked like. The decode warning named the film but
/// not the file, which left two bad covers to be found among thousands by hand. The HDR
/// disagreement was one line per item, and since the disagreement is the ordinary case - 109 of
/// 291 HDR titles on the reference library - it produced over a hundred lines a run and buried the
/// two warnings that mattered.
/// </remarks>
[Collection(BaseItemStaticsCollection.Name)]
public class ReportingTests : IDisposable
{
    private readonly string _folder;

    public ReportingTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "poster-overlays-report-" + Guid.NewGuid().ToString("N"));
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
    /// The point of the change: the path, not just the title.
    /// </summary>
    [Fact]
    public async Task TheDecodeWarningNamesTheFile()
    {
        // A file that is not an image at all: SkiaSharp returns null, which is the same shape as
        // the two concatenated JPEGs found on the reference library.
        string releaseFolder = Path.Combine(_folder, "A.Film.2020.2160p.BluRay.x265-GRP");
        Directory.CreateDirectory(releaseFolder);
        string imagePath = Path.Combine(releaseFolder, "poster.jpg");
        File.WriteAllBytes(imagePath, Encoding.UTF8.GetBytes("this is not a JPEG"));

        var logger = new CollectingLogger();
        var movie = NewMovie(releaseFolder, imagePath);
        BaseItem.MediaSourceManager = FakeMediaSourceManager.WithStreams(
            new MediaStream { Type = MediaStreamType.Video, Width = 3840, Height = 2160 });

        var applier = NewApplier(logger, Enabled(allowResolution: true));
        var outcome = await applier.ApplyAsync(movie, CancellationToken.None);

        Assert.Equal(OverlayOutcome.Failed, outcome);
        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.Single(warnings);
        Assert.Contains(imagePath, warnings[0].Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The common direction: the stream carries HDR, the folder name is silent about it. Counted,
    /// not named, and above all not logged per item.
    /// </summary>
    [Fact]
    public async Task AStreamCarryingHdrTheNameOmitsIsCountedAndNotLogged()
    {
        var logger = new CollectingLogger();
        var applier = NewApplier(logger, Enabled(allowResolution: true));

        await ApplyOne(applier, "A.Film.2020.2160p.BluRay.x265-GRP", streamHasHdr: true);

        Assert.Equal(1, applier.StreamHasHdrWithoutFolder);
        Assert.Empty(applier.FolderClaimsHdrWithoutStream);
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("HDR", StringComparison.Ordinal));
    }

    /// <summary>
    /// The direction worth a name: the folder promises HDR the file does not have.
    /// </summary>
    [Fact]
    public async Task AFolderClaimingHdrTheStreamLacksIsNamed()
    {
        var logger = new CollectingLogger();
        var applier = NewApplier(logger, Enabled(allowResolution: true));

        await ApplyOne(applier, "A.Film.2020.2160p.HDR.BluRay.x265-GRP", streamHasHdr: false);

        Assert.Equal(0, applier.StreamHasHdrWithoutFolder);
        Assert.Single(applier.FolderClaimsHdrWithoutStream);
        Assert.Equal("A film", applier.FolderClaimsHdrWithoutStream[0]);
    }

    /// <summary>
    /// Switched off - the default - nothing is collected at all, however loudly the folder name
    /// and the stream disagree. A library that names folders "Film (Year)" would otherwise have
    /// every HDR title counted as a mismatch.
    /// </summary>
    [Fact]
    public async Task NothingIsCollectedWhenTheReportIsOff()
    {
        var applier = NewApplier(new CollectingLogger(), Enabled(allowResolution: true, reportHdr: false));

        await ApplyOne(applier, "A.Film.2020.2160p.BluRay.x265-GRP", streamHasHdr: true);
        await ApplyOne(applier, "B.Film.2019.2160p.HDR.BluRay.x265-GRP", streamHasHdr: false);

        Assert.Equal(0, applier.StreamHasHdrWithoutFolder);
        Assert.Empty(applier.FolderClaimsHdrWithoutStream);
    }

    /// <summary>
    /// And the default really is off, so the setting cannot be quietly on for everyone.
    /// </summary>
    [Fact]
    public void TheReportIsOffByDefault() =>
        Assert.False(new PluginConfiguration().ReportHdrNameMismatches);

    /// <summary>
    /// And agreement produces neither, so the tally cannot quietly count everything.
    /// </summary>
    [Fact]
    public async Task AgreementIsNotCounted()
    {
        var applier = NewApplier(new CollectingLogger(), Enabled(allowResolution: true));

        await ApplyOne(applier, "A.Film.2020.2160p.HDR.BluRay.x265-GRP", streamHasHdr: true);
        await ApplyOne(applier, "B.Film.2019.1080p.BluRay.x264-GRP", streamHasHdr: false);

        Assert.Equal(0, applier.StreamHasHdrWithoutFolder);
        Assert.Empty(applier.FolderClaimsHdrWithoutStream);
    }

    private static PluginConfiguration Enabled(bool allowResolution, bool reportHdr = true)
    {
        var config = new PluginConfiguration
        {
            DryRun = true,

            // Off by default in the product, so the tests that assert on the tally have to ask
            // for it. That is the right way round: the default is what most libraries want.
            ReportHdrNameMismatches = reportHdr,
        };
        config.Movies.Enabled = true;
        config.Movies.AllowAudio = false;
        config.Movies.AllowResolution = allowResolution;
        return config;
    }

    private OverlayApplier NewApplier(ILogger logger, PluginConfiguration config) =>
        new(null!, null!, logger, config, new OverlayStateStore(_folder));

    private static Movie NewMovie(string releaseFolder, string? imagePath) => new()
    {
        Id = Guid.NewGuid(),
        Name = "A film",
        Path = Path.Combine(releaseFolder, "film.mkv"),
        ImageInfos = imagePath is null
            ? []
            : [new ItemImageInfo { Type = ImageType.Primary, Path = imagePath }],
    };

    /// <summary>
    /// A video stream that really reports the wanted range.
    /// </summary>
    /// <remarks>
    /// <c>MediaStream.VideoRangeType</c> is derived and cannot be assigned, so the colour fields
    /// underneath it are set instead - the same pair a neighbouring session measured on every one
    /// of 109 HDR titles on the reference library. The assertion below is not decoration: without
    /// it a stream that Jellyfin reads as SDR would make the "folder claims HDR the stream lacks"
    /// test pass for the wrong reason.
    /// <para>
    /// <b>Dolby Vision needs a third field on Jellyfin 12.</b> The derivation there downgrades a
    /// recognised DV profile to <c>DOVIInvalid</c> unless the stream also carries
    /// <c>ColorSpace == "bt2020nc"</c> alongside <c>ColorPrimaries == "bt2020"</c>; on 10.11 that
    /// step does not exist. HDR10 as built here is unaffected, but a DV case added later without
    /// the ColorSpace would be classified as invalid on 12 and fine on 10.11 - which the assertion
    /// would catch, and this note explains. The plugin itself never populates these fields; it
    /// reads whatever the server put there, which is why only the tests have to know this.
    /// </para>
    /// </remarks>
    /// <param name="hdr">True for an HDR10 stream, false for SDR.</param>
    /// <returns>The stream.</returns>
    private static MediaStream VideoStream(bool hdr)
    {
        var stream = new MediaStream
        {
            Type = MediaStreamType.Video,
            Width = 3840,
            Height = 2160,
            ColorTransfer = hdr ? "smpte2084" : "bt709",
            ColorPrimaries = hdr ? "bt2020" : "bt709",
        };

        Assert.Equal(
            hdr ? VideoRangeType.HDR10 : VideoRangeType.SDR,
            stream.VideoRangeType);
        return stream;
    }

    /// <summary>
    /// Runs one item through far enough to reach the HDR comparison. No image on purpose: the
    /// comparison happens before the picture is looked at, so the outcome is NoImage and the tally
    /// is still filled - which keeps these tests off the rendering path entirely.
    /// </summary>
    private async Task ApplyOne(OverlayApplier applier, string releaseFolderName, bool streamHasHdr)
    {
        string releaseFolder = Path.Combine(_folder, releaseFolderName);
        Directory.CreateDirectory(releaseFolder);
        BaseItem.MediaSourceManager = FakeMediaSourceManager.WithStreams(VideoStream(streamHasHdr));

        var outcome = await applier.ApplyAsync(NewMovie(releaseFolder, imagePath: null), CancellationToken.None);
        Assert.Equal(OverlayOutcome.NoImage, outcome);
    }
}
