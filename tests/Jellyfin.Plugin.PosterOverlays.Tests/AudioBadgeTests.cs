using System;
using System.Collections.Generic;
using Jellyfin.Plugin.PosterOverlays.Badges;
using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// The audio format label.
/// </summary>
/// <remarks>
/// The cases with real values in them come from the reference library, not from imagination:
/// of 105 groups that share one film, 7 differ in nothing but the audio format and a further 2
/// only in the channel layout. Inventing inputs here would have missed both shapes - the Atmos
/// track whose codec says <c>eac3</c>, and the pair that is DTS on both sides.
/// </remarks>
public class AudioBadgeTests
{
    /// <summary>
    /// Atmos is found in the profile or the title, never in the codec.
    /// </summary>
    /// <remarks>
    /// This is the case that a codec-only lookup misses entirely: an Atmos stream is carried
    /// inside TrueHD or E-AC-3 and reports itself as one of those.
    /// </remarks>
    /// <param name="codec">The codec field.</param>
    /// <param name="profile">The profile field.</param>
    /// <param name="title">The track title.</param>
    [Theory]
    [InlineData("eac3", "", "Deutsch (Dolby Atmos)")]
    [InlineData("truehd", "Dolby TrueHD + Dolby Atmos", "")]
    [InlineData("eac3", "Dolby Digital+ with Dolby Atmos", "")]
    public void AtmosBeatsItsContainer(string codec, string profile, string title)
    {
        var tracks = new List<AudioTrack> { new(codec, profile, title, 8) };
        Assert.Equal("ATMOS", TechnicalBadges.Audio(tracks, withChannels: false));
    }

    /// <summary>
    /// The specific formats win over the general ones they travel inside.
    /// </summary>
    /// <param name="codec">The codec field.</param>
    /// <param name="profile">The profile field.</param>
    /// <param name="expected">The label that should come out.</param>
    [Theory]
    [InlineData("dts", "DTS-HD MA", "DTS-HD")]
    [InlineData("dts", "DTS-ES", "DTS-HD")]
    [InlineData("dts", "DTS:X", "DTS-X")]
    [InlineData("dts", "DTS", "DTS")]
    [InlineData("truehd", "", "TRUEHD")]
    [InlineData("eac3", "", "EAC3")]
    [InlineData("ac3", "", "AC3")]
    [InlineData("flac", "", "FLAC")]
    [InlineData("aac", "LC", "AAC")]
    public void TheMoreSpecificFormatWins(string codec, string profile, string expected)
    {
        var tracks = new List<AudioTrack> { new(codec, profile, null, 6) };
        Assert.Equal(expected, TechnicalBadges.Audio(tracks, withChannels: false));
    }

    /// <summary>
    /// Only the best track is reported, whatever order the tracks arrive in.
    /// </summary>
    /// <remarks>
    /// A film with Atmos, a DTS-HD track and a stereo commentary is an Atmos film. Listing all
    /// three would be true and useless, and the order the container happens to use is not a
    /// ranking - hence the reversed case.
    /// </remarks>
    [Fact]
    public void OnlyTheBestTrackCounts()
    {
        var forwards = new List<AudioTrack>
        {
            new("eac3", "", "Dolby Atmos", 8),
            new("dts", "DTS-HD MA", null, 6),
            new("aac", "LC", "Commentary", 2),
        };

        var backwards = new List<AudioTrack>
        {
            new("aac", "LC", "Commentary", 2),
            new("dts", "DTS-HD MA", null, 6),
            new("eac3", "", "Dolby Atmos", 8),
        };

        Assert.Equal("ATMOS", TechnicalBadges.Audio(forwards, withChannels: false));
        Assert.Equal("ATMOS", TechnicalBadges.Audio(backwards, withChannels: false));
    }

    /// <summary>
    /// The channel layout is spoken as it is written on a box: six channels are 5.1.
    /// </summary>
    /// <param name="channels">The channel count from the stream.</param>
    /// <param name="expected">The label including the layout.</param>
    [Theory]
    [InlineData(8, "DTS 7.1")]
    [InlineData(7, "DTS 6.1")]
    [InlineData(6, "DTS 5.1")]
    [InlineData(2, "DTS 2.0")]
    [InlineData(1, "DTS MONO")]
    public void ChannelsAreNamedTheWayTheyAreSpoken(int channels, string expected)
    {
        var tracks = new List<AudioTrack> { new("dts", "DTS", null, channels) };
        Assert.Equal(expected, TechnicalBadges.Audio(tracks, withChannels: true));
    }

    /// <summary>
    /// The measured case the second level exists for: both copies are DTS, and only the channel
    /// count differs. Taken from two entries of Evangelion 2.0 on the reference library.
    /// </summary>
    [Fact]
    public void ChannelsSeparateWhatTheFormatCannot()
    {
        var five = new List<AudioTrack> { new("dts", "DTS", null, 6) };
        var six = new List<AudioTrack> { new("dts", "DTS-ES", null, 7) };

        // Coarse: DTS-ES is the extended variant, so these already differ. The point of the test
        // is the pair below it, where the profile is equal too.
        Assert.NotEqual(TechnicalBadges.Audio(five, false), TechnicalBadges.Audio(six, false));

        var plainFive = new List<AudioTrack> { new("dts", "DTS", null, 6) };
        var plainSeven = new List<AudioTrack> { new("dts", "DTS", null, 8) };

        Assert.Equal(TechnicalBadges.Audio(plainFive, false), TechnicalBadges.Audio(plainSeven, false));
        Assert.NotEqual(TechnicalBadges.Audio(plainFive, true), TechnicalBadges.Audio(plainSeven, true));
    }

    /// <summary>
    /// The case the language preference exists for: German AC3 beside English DTS.
    /// </summary>
    /// <remarks>
    /// Measured on the reference library, 437 films are shaped like this. Without a preference the
    /// badge says DTS, which is true about the file and about a track the viewer never selects.
    /// </remarks>
    [Fact]
    public void ThePreferredLanguageDecidesRatherThanTheBestTrack()
    {
        var tracks = new List<AudioTrack>
        {
            new("ac3", "", null, 6, "deu"),
            new("dts", "DTS-HD MA", null, 8, "eng"),
        };

        Assert.Equal("DTS-HD", TechnicalBadges.Audio(tracks, false));
        Assert.Equal("AC3", TechnicalBadges.Audio(tracks, false, ["deu"]));
        Assert.Equal("DTS-HD", TechnicalBadges.Audio(tracks, false, ["eng"]));
    }

    /// <summary>
    /// One track is never filtered away, whatever it is tagged with.
    /// </summary>
    /// <remarks>
    /// A file with a single English track still says what it has. Filtering it out because the
    /// preference says German would turn a working badge into silence and gain nothing - there is
    /// no other track it could have picked instead.
    /// </remarks>
    [Fact]
    public void ASingleTrackAlwaysCounts()
    {
        Assert.Equal("DTS", TechnicalBadges.Audio([new("dts", "DTS", null, 6, "eng")], false, ["deu"]));
        Assert.Equal("DTS", TechnicalBadges.Audio([new("dts", "DTS", null, 6, null)], false, ["deu"]));
    }

    /// <summary>
    /// When no track is in a preferred language, all of them are considered again.
    /// </summary>
    /// <remarks>
    /// The alternative - no badge at all on a film with no German track - would hide the very case
    /// where two copies differ in whether they are dubbed.
    /// </remarks>
    [Fact]
    public void NoMatchFallsBackToEverything()
    {
        var tracks = new List<AudioTrack>
        {
            new("ac3", "", null, 6, "jpn"),
            new("dts", "DTS-HD MA", null, 8, "eng"),
        };

        Assert.Equal("DTS-HD", TechnicalBadges.Audio(tracks, false, ["deu"]));
    }

    /// <summary>
    /// The preference is a list, and earlier entries win.
    /// </summary>
    [Fact]
    public void TheFirstListedLanguageThatExistsWins()
    {
        var tracks = new List<AudioTrack>
        {
            new("ac3", "", null, 6, "deu"),
            new("truehd", "", null, 8, "eng"),
        };

        Assert.Equal("AC3", TechnicalBadges.Audio(tracks, false, ["deu", "eng"]));
        Assert.Equal("TRUEHD", TechnicalBadges.Audio(tracks, false, ["fra", "eng", "deu"]));
    }

    /// <summary>
    /// Both ISO 639-2 codes for a language mean the same language, and so do the short forms.
    /// </summary>
    /// <remarks>
    /// German really does have two codes - <c>ger</c> and <c>deu</c> - and files in the wild use
    /// both. Measured on the reference library the streams say <c>deu</c>, but somebody typing the
    /// setting is at least as likely to write <c>de</c> or <c>german</c>. If those did not all
    /// arrive at one key, the setting would work or not depending on a spelling nobody chose.
    /// </remarks>
    /// <param name="streamCode">What the stream says.</param>
    /// <param name="typed">What somebody typed into the setting.</param>
    [Theory]
    [InlineData("deu", "de")]
    [InlineData("deu", "ger")]
    [InlineData("deu", "German")]
    [InlineData("ger", "deu")]
    [InlineData("fra", "fre")]
    [InlineData("nld", "dut")]
    public void LanguageSpellingsAreTreatedAsOne(string streamCode, string typed)
    {
        var tracks = new List<AudioTrack>
        {
            new("ac3", "", null, 6, streamCode),
            new("dts", "DTS-HD MA", null, 8, "eng"),
        };

        Assert.Equal("AC3", TechnicalBadges.Audio(tracks, false, [typed]));
    }

    /// <summary>
    /// Nothing to say produces nothing, rather than an empty pill.
    /// </summary>
    [Fact]
    public void SilenceIsNotABadge()
    {
        Assert.Null(TechnicalBadges.Audio(Array.Empty<AudioTrack>(), false));
        Assert.Null(TechnicalBadges.Audio(new List<AudioTrack> { new("pcm_s16le", null, null, 2) }, false));
        Assert.Null(TechnicalBadges.Audio(new List<AudioTrack> { new(null, null, null, null) }, false));
    }

    /// <summary>
    /// A stream that does not report its channels still gets its format.
    /// </summary>
    /// <remarks>
    /// Asking for channels is a request, not a requirement. Falling back to the coarse label is
    /// right: the alternative is inventing "5.1" or dropping a badge that was earned.
    /// </remarks>
    [Fact]
    public void MissingChannelsFallBackToTheFormat()
    {
        var tracks = new List<AudioTrack> { new("truehd", null, null, null) };
        Assert.Equal("TRUEHD", TechnicalBadges.Audio(tracks, withChannels: true));
    }
}
