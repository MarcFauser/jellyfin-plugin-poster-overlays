using Jellyfin.Plugin.PosterOverlays.Badges;
using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// The parser's acceptance suite. Every folder name below is either taken verbatim from a real
/// library of 2378 movie folders, or is a collision case that a source verified as a real film
/// title. Both directions matter: a rule that only ever fires proves nothing.
/// </summary>
public class FolderNameParserTests
{
    [Theory]
    // --- the case the whole plugin exists for: two folders, one film -----------------
    [InlineData("Avatar.Aufbruch.nach.Pandora.Extended.2009.German.DTS.1080p.BluRay.x264-SoW", "Avatar - Aufbruch nach Pandora", null, "EXT")]
    [InlineData("Avatar.Aufbruch.nach.Pandora.2009.German.DTS.1080p.BluRay.x264-SoW", "Avatar - Aufbruch nach Pandora", null, null)]

    // --- edition BEFORE the year. 70 of 168 real cases look like this, so a rule that
    //     only searches after the year would lose them -------------------------------
    [InlineData("Sucker.Punch.EXTENDED.2011.German.DTS.720p.BluRay.x264-BRiGHT", "Sucker Punch", null, "EXT")]
    [InlineData("Baywatch.Extended.2017.German.DL.AC3.1080p.BluRay.x264-EmpireHD", "Baywatch", null, "EXT")]
    [InlineData("Alien.1.Special.Edition.1979.German.DL.DTS.1080p.BluRay.x264-MOViESTARS", "Alien", null, "SE")]
    [InlineData("Kampfstern.Galactica.Der.Kinofilm.Extended.Cut.1978.German.DL.1080p.BluRay.x264-ETM", "Kampfstern Galactica - Der Kinofilm", null, "EXT")]
    [InlineData("Sie.nannten.ihn.Knochenbrecher.UNCUT.1978.German.ML.AC3D.1080p.BluRay.x264-BM", "Sie nannten ihn Knochenbrecher", null, "UC")]
    [InlineData("Mission.Impossible.Fallout.IMAX.2018.German.DL.AC3.1080p.BluRay.x265-FuN", "Mission: Impossible - Fallout", null, "IMAX")]

    // --- edition AFTER the year -----------------------------------------------------
    [InlineData("Harry.Potter.und.die.Kammer.des.Schreckens.2002.Extended.German.DL.AC3.720p.BluRay.x264-MOViEADDiCTS", "Harry Potter und die Kammer des Schreckens", null, "EXT")]
    [InlineData("Aquaman.2018.IMAX.German.DTS.DL.1080p.BluRay.x265-UNFIrED", "Aquaman", null, "IMAX")]
    [InlineData("Lazer.Team.2015.Directors.Cut.German.DL.DTS.1080p.BluRay.x264-CONTRiBUTiON", "Lazer Team", null, "DC")]
    [InlineData("Escape.Room.2.No.Way.Out.2021.EXTENDED.German.DTS.DL.1080p.BluRay.x265-HDSource", "Escape Room 2: No Way Out", null, "EXT")]

    // --- a foreign token sits between the edition and the year ----------------------
    [InlineData("Guardians.of.the.Galaxy.Vol.2.IMAX.HYBRiD.2017.German.DTSHD.DL.1080p.BluRay.x264-Pate", "Guardians of the Galaxy Vol. 2", null, "IMAX")]

    // --- two editions in one folder: the higher priority wins ------------------------
    [InlineData("Die.Unendliche.Geschichte.1984.UNCUT.Remastered.German.DTSHD.DL.1080p.BluRay.x264-iNCEPTiON", "Die unendliche Geschichte", null, "UC")]
    [InlineData("Dragonball.Z.Movie.02.Der.Stärkste.auf.Erden.Remastered.UNCUT.1990.German.AC3.DUBBED.ML.720p.BluRay.x264-STARS", "Dragonball Z - Movie 02: Der Stärkste auf Erden", null, "UC")]

    // --- the pair whose folder names differ in nothing but the edition word ----------
    [InlineData("Der.toedliche.Schwarm.1978.KiNOFASSUNG.German.DL.1080p.BluRay.x264-PL3X", "Der tödliche Schwarm", null, "THR")]
    [InlineData("Der.toedliche.Schwarm.1978.LANGFASSUNG.German.DL.1080p.BluRay.x264-PL3X", "Der tödliche Schwarm", null, "EXT")]

    // --- umlaut in the folder, transliteration in the sibling, and vice versa --------
    [InlineData("Zurueck.in.die.Zukunft.1.1985.REMASTERED.German.EAC3.DL.1080p.BluRay.x265-VECTOR", "Zurück in die Zukunft", null, "REM")]

    // --- no year at all. Real: one folder in the reference library has none ----------
    [InlineData("Eden.Lake.UNCUT.DL.1080p.BluRay.x264-CTWHD", "Eden Lake", null, "UC")]

    // --- bare DC. All six real occurrences are genuine director's cuts ---------------
    [InlineData("Supergirl.1984.DC.German.DL.1080p.BluRay.x264-CONTRiBUTiON", "Supergirl", null, "DC")]
    [InlineData("Fear.And.Loathing.in.Las.Vegas.DC.1998.1080p.BluRay.DTS.x264.dxva-HDC", "Fear and Loathing in Las Vegas", null, "DC")]
    [InlineData("Armee.der.Finsternis.DC.1992.German.DL.AC3.1080p.BluRay.x265-FuN", "Armee der Finsternis", "Army of Darkness", "DC")]
    [InlineData("Fast.and.Furious.9.2021.DC.German.DL.DTS.1080p.BluRay.x265-HDSource", "Fast & Furious 9", null, "DC")]

    // --- and the collisions the guards exist for -------------------------------------
    [InlineData("DC.League.of.Super-Pets.2022.German.DL.1080p.BluRay.x264-GROUP", "DC League of Super-Pets", null, null)]
    [InlineData("Some.Movie.2010.dc.German.DL.1080p.BluRay.x264-GROUP", "Some Movie", null, null)]
    [InlineData("Uncut.Gems.2019.German.DL.1080p.WEBRiP.x264-LAW", "Der schwarze Diamant", "Uncut Gems", null)]
    [InlineData("Der.Schwarze.Diamant.2019.German.DL.AC3.Dubbed.1080p.BluRay.x264-muhHD", "Der schwarze Diamant", "Uncut Gems", null)]
    [InlineData("Directors.Cut.2016.German.DL.1080p.BluRay.x264-GROUP", "Director's Cut", null, null)]
    [InlineData("The.Final.Cut.2004.German.DL.1080p.BluRay.x264-GROUP", "The Final Cut", null, null)]
    [InlineData("Black.and.White.1999.German.1080p.WEB.H264-GROUP", "Black and White", null, null)]
    [InlineData("Blade.Runner.2049.2017.German.DL.2160p.UHD.BluRay.x265-GROUP", "Blade Runner 2049", null, null)]
    [InlineData("Unrated.The.Movie.German.2009.DL.DTS.1080p.BluRay.x264-GOREHOUNDS", "Unrated: The Movie", null, null)]
    [InlineData("Some.Movie.2008.NON-IMAX.German.DL.1080p.BluRay.x264-GROUP", "Some Movie", null, null)]

    // --- the title token is suppressed, a real one next to it still survives ---------
    [InlineData("Uncut.Gems.2019.EXTENDED.German.DL.1080p.WEBRiP.x264-LAW", "Der schwarze Diamant", "Uncut Gems", "EXT")]
    public void ParsesEdition(string folder, string? name, string? originalTitle, string? expected)
    {
        var result = FolderNameParser.Parse(folder, name, originalTitle);
        Assert.Equal(expected, result.Edition);
    }

    [Theory]
    // A real folder from the reference library. 3D sits before the year, like so much else here.
    [InlineData("Störche.Abenteuer.im.Anflug.3D.2016.German.AC3D.DL1080p.BluRay.x264-PsO", "Störche - Abenteuer im Anflug", "3D")]
    [InlineData("Some.Movie.2016.German.HSBS.1080p.BluRay.x264-GROUP", "Some Movie", "3D")]
    [InlineData("Some.Movie.2016.German.Half-SBS.1080p.BluRay.x264-GROUP", "Some Movie", "3D")]
    [InlineData("Some.Movie.2016.German.MVC.1080p.BluRay-GROUP", "Some Movie", "3D")]
    [InlineData("Some.Movie.2016.German.SBS.1080p.BluRay.x264-GROUP", "Some Movie", "3D")]
    // TAB is a word as well as a packing format, so it only counts in capitals.
    [InlineData("Some.Movie.2016.German.TAB.1080p.BluRay.x264-GROUP", "Some Movie", "3D")]
    [InlineData("Some.Movie.2016.German.tab.1080p.BluRay.x264-GROUP", "Some Movie", null)]
    // And a film whose title carries the token: subtraction removes it before the rules look.
    [InlineData("Spy.Kids.3D.Game.Over.2003.German.DL.1080p.BluRay.x264-GROUP", "Spy Kids 3D: Game Over", null)]
    [InlineData("Avatar.Aufbruch.nach.Pandora.2009.German.DTS.1080p.BluRay.x264-SoW", "Avatar - Aufbruch nach Pandora", null)]
    public void ParsesPresentationFormat(string folder, string? name, string? expected)
    {
        var result = FolderNameParser.Parse(folder, name, null);
        Assert.Equal(expected, result.Format);
    }

    [Theory]
    [InlineData("Der.Super.Mario.Galaxy.Film.2026.PROPER.German.TELESYNC.1080p.x264-GHOST", "Der Super Mario Galaxy Film", "TS")]
    [InlineData("Oppenheimer.2023.TS.LD.German.1080p.x264-PsO", "Oppenheimer", "TS")]
    [InlineData("Cam.2018.German.DL.1080p.WEB.x264-GROUP", "Cam", null)]
    [InlineData("Some.Movie.2019.CAM.German.1080p.x264-GROUP", "Some Movie", "CAM")]
    [InlineData("Avatar.Aufbruch.nach.Pandora.2009.German.DTS.1080p.BluRay.x264-SoW", "Avatar - Aufbruch nach Pandora", null)]
    public void ParsesSourceQuality(string folder, string? name, string? expected)
    {
        var result = FolderNameParser.Parse(folder, name, null);
        Assert.Equal(expected, result.Source);
    }

    /// <summary>
    /// 186 of 2378 items in the reference library have no metadata match at all: their name is
    /// the folder name. Title subtraction then eats everything, so the parser has to notice and
    /// fall back - without this the guard silently suppresses every badge and looks successful.
    /// </summary>
    [Fact]
    public void FallsBackWhenTheTitleIsTheFolderName()
    {
        const string Folder = "Deadpool.2.Kinofassung.2018.German.DL.AC3.1080p.BluRay.x265-FuN";

        var result = FolderNameParser.Parse(Folder, Folder, null);

        Assert.False(result.TitleTrusted);
        Assert.Equal("THR", result.Edition);
    }

    [Fact]
    public void TrustsAProperTitle()
    {
        var result = FolderNameParser.Parse(
            "Avatar.Aufbruch.nach.Pandora.Extended.2009.German.DTS.1080p.BluRay.x264-SoW",
            "Avatar - Aufbruch nach Pandora",
            null);

        Assert.True(result.TitleTrusted);
        Assert.Equal("extended 2009 german dts 1080p bluray x264 sow", result.TagZone);
    }
}
