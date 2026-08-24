using Jellyfin.Plugin.PosterOverlays.Badges;
using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// Reading an edition out of an episode's file name.
/// </summary>
/// <remarks>
/// The pair that motivated the whole thing comes first. Everything after it guards the two ways
/// this can go wrong: missing a real edition, and inventing one out of an episode title.
/// </remarks>
public class EpisodeFileNameParserTests
{
    /// <summary>
    /// The measured case: two copies of one episode in the same resolution, where every technical
    /// badge reads the same and only the edition token tells them apart.
    /// </summary>
    [Fact]
    public void TheTwoBuckRogersFilesAreToldApart()
    {
        var plain = EpisodeFileNameParser.Parse(
            "buck.rogers.s01e01.german.dl.1080p.fs.bluray.x264-excited.mkv", null);

        var alternate = EpisodeFileNameParser.Parse(
            "buck.rogers.s01e01e02.german.dl.alternate.cut.1080p.bluray.x264-excited.mkv", null);

        Assert.Null(plain.Edition);
        Assert.Equal("ALT", alternate.Edition);
    }

    [Theory]
    [InlineData("Show.S01E01.Extended.German.DL.1080p.BluRay.x264-GRP.mkv", "EXT")]
    [InlineData("Show.S01E01.Uncut.German.DL.1080p.BluRay.x264-GRP.mkv", "UC")]
    [InlineData("Show.s01e05.directors.cut.german.dl.720p.webrip.x264-grp.mkv", "DC")]
    [InlineData("Show.1x07.Remastered.German.DL.1080p.BluRay.x264-GRP.mkv", "REM")]
    [InlineData("Show.S01.E09.Theatrical.German.DL.1080p.BluRay.x264-GRP.mkv", "THR")]
    [InlineData("Show.S02E11-E12.Special.Edition.German.DL.1080p.BluRay.x264-GRP.mkv", "SE")]
    public void TheAnchorIsFoundInEverySpellingThatOccurs(string fileName, string expected)
    {
        Assert.Equal(expected, EpisodeFileNameParser.Parse(fileName, null).Edition);
    }

    /// <summary>
    /// The guard that matters. An anchor says where the series title ends; it says nothing about
    /// what follows, and what follows is usually the episode title.
    /// </summary>
    /// <remarks>
    /// All six of these fire a catalogue rule when read as bare text - measured against the
    /// catalogue, twelve of twelve plausible titles do. Subtracting the episode title is what
    /// stops them, exactly as subtracting the film title does for movies.
    /// </remarks>
    [Theory]
    [InlineData("Show.S01E03.Final.Cut.German.DL.1080p.BluRay.x264-GRP.mkv", "Final Cut")]
    [InlineData("Show.S01E04.The.Extended.Family.German.DL.1080p.BluRay.x264-GRP.mkv", "The Extended Family")]
    [InlineData("Show.S01E05.Restored.German.DL.1080p.BluRay.x264-GRP.mkv", "Restored")]
    [InlineData("Show.S01E06.Recut.German.DL.1080p.BluRay.x264-GRP.mkv", "Recut")]
    [InlineData("Show.S01E07.Redux.German.DL.1080p.BluRay.x264-GRP.mkv", "Redux")]
    [InlineData("Show.S01E08.Uncut.German.DL.1080p.BluRay.x264-GRP.mkv", "Uncut")]
    public void AnEpisodeTitleThatLooksLikeAnEditionIsSubtracted(string fileName, string title)
    {
        Assert.Null(EpisodeFileNameParser.Parse(fileName, title).Edition);
    }

    /// <summary>
    /// And the control for the test above: with the title unknown, those very names do fire. If
    /// they did not, the subtraction would be proving nothing.
    /// </summary>
    [Theory]
    [InlineData("Show.S01E03.Final.Cut.German.DL.1080p.BluRay.x264-GRP.mkv", "FIN")]
    [InlineData("Show.S01E04.The.Extended.Family.German.DL.1080p.BluRay.x264-GRP.mkv", "EXT")]
    [InlineData("Show.S01E05.Restored.German.DL.1080p.BluRay.x264-GRP.mkv", "REM")]
    public void WithoutTheTitleThoseSameNamesDoFire(string fileName, string expected)
    {
        Assert.Equal(expected, EpisodeFileNameParser.Parse(fileName, null).Edition);
    }

    /// <summary>
    /// The second guard: a zone with no release tag in it is all episode title, so it is dropped
    /// whole rather than searched. Affordable only because the anchor is hard.
    /// </summary>
    [Fact]
    public void AZoneWithoutAnyReleaseTagIsNotSearched()
    {
        // "Uncut" here is the whole of what follows the anchor, and nothing around it says
        // release. A scene name never looks like this.
        var result = EpisodeFileNameParser.Parse("Show.S01E02.Uncut.mkv", null);

        Assert.Null(result.Edition);
        Assert.Equal(string.Empty, result.TagZone);
    }

    /// <summary>
    /// The control for that one: add release tags and the same token is found.
    /// </summary>
    [Fact]
    public void TheSameTokenIsFoundOnceTheZoneLooksLikeARelease()
    {
        Assert.Equal("UC", EpisodeFileNameParser.Parse("Show.S01E02.Uncut.1080p.BluRay.mkv", null).Edition);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Show.Without.Any.Marker.1080p.BluRay.x264-GRP.mkv")]
    [InlineData("Movie.Extended.2009.German.DL.1080p.BluRay.x264-GRP.mkv")]
    public void WithoutAnAnchorNothingIsClaimed(string? fileName)
    {
        var result = EpisodeFileNameParser.Parse(fileName, null);

        Assert.Null(result.Edition);
        Assert.Null(result.Source);
        Assert.Null(result.Format);
        Assert.False(result.TitleTrusted);
    }

    /// <summary>
    /// A full path is accepted as readily as a bare name, because the caller hands over
    /// <c>item.Path</c>.
    /// </summary>
    [Fact]
    public void AFullPathWorksTheSameAsABareName()
    {
        const string Path = "/mnt/library/Show/Season 01/Show.S01E01.Extended.German.DL.1080p.BluRay.x264-GRP.mkv";

        Assert.Equal("EXT", EpisodeFileNameParser.Parse(Path, null).Edition);
    }

    /// <summary>
    /// The source vocabulary rides along, and the capitals rule with it.
    /// </summary>
    [Fact]
    public void SourceQualityIsReadTooAndShortTokensStillNeedCapitals()
    {
        Assert.Equal("TS", EpisodeFileNameParser.Parse("Show.S01E01.German.TS.1080p.x264-GRP.mkv", null).Source);
        Assert.Null(EpisodeFileNameParser.Parse("Show.S01E01.German.ts.1080p.x264-GRP.mkv", null).Source);
    }

    /// <summary>
    /// A season folder name must not be mistaken for a marker, because the fallback hands one to
    /// the folder parser and a false anchor there would cut the zone in the wrong place.
    /// </summary>
    [Fact]
    public void SeasonAloneIsNotAnAnchor()
    {
        Assert.Null(EpisodeFileNameParser.Parse("Show.S01.German.DL.1080p.BluRay.x264-GRP.mkv", null).Edition);
    }
}
