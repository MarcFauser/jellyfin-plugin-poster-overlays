using System.Linq;
using Jellyfin.Plugin.PosterOverlays.Badges;
using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// Two edition badges where a folder makes two independent statements, and one where it makes the
/// same statement twice.
/// </summary>
/// <remarks>
/// The three pairs asserted here are the only ones that occur: measured over 2380 real folders,
/// six carry two edition tokens - REM+UC four times, OM+UC once, EXT+REM once. Every one is a cut
/// plus something that is not a cut, which is why the rule is one badge per facet rather than a
/// count.
/// </remarks>
public class EditionFacetTests
{
    [Theory]
    [InlineData("Ritter.aus.Leidenschaft.2001.REMASTERED.EXTENDED.German.OPUS.DL.2160p.HDR.BluRay.x265.10bit-FuN",
                "Ritter aus Leidenschaft", "EXT", "REM")]
    [InlineData("Die.Unendliche.Geschichte.1984.UNCUT.Remastered.German.DTSHD.DL.1080p.BluRay.x264-iNCEPTiON",
                "Die Unendliche Geschichte", "UC", "REM")]
    [InlineData("Jumper.UNCUT.OPEN.MATTE.2008.German.AC3D.DL.1080p.UK-BluRay.x265-FuN",
                "Jumper", "UC", "OM")]
    public void AFolderThatSaysTwoThingsGetsTwoBadges(string folder, string title, string first, string second)
    {
        var result = FolderNameParser.Parse(folder, title, null);

        Assert.Equal(new[] { first, second }, result.Editions);

        // The leading badge stays the cut, because that is what tells two copies apart and what
        // survives if MaxBadges trims the row.
        Assert.Equal(first, result.Edition);
    }

    /// <summary>
    /// The counter-case, and the reason facets exist rather than "collect everything": two rules
    /// that describe the same cut still collapse to one badge.
    /// </summary>
    [Theory]
    [InlineData("Blade.Runner.Extended.Directors.Cut.1982.German.DL.1080p.BluRay.x264-GRP", "Blade Runner", "EDC")]
    [InlineData("Payback.Directors.Cut.1999.German.DL.1080p.BluRay.x264-GRP", "Payback", "DC")]
    [InlineData("Avatar.Extended.2009.German.DTS.1080p.BluRay.x264-SoW", "Avatar", "EXT")]
    public void AFolderThatSaysOneThingTwiceGetsOneBadge(string folder, string title, string expected)
    {
        var result = FolderNameParser.Parse(folder, title, null);

        Assert.Equal(new[] { expected }, result.Editions);
    }

    /// <summary>
    /// Every rule has to declare a facet that is one of the three, or a token would silently never
    /// be reachable.
    /// </summary>
    [Fact]
    public void EveryEditionRuleSitsInAKnownFacet()
    {
        var facets = new[] { EditionFacet.Cut, EditionFacet.Presentation, EditionFacet.Master };

        Assert.All(EditionCatalog.Editions, r => Assert.Contains(r.Facet, facets));
        Assert.All(EditionCatalog.EditionCapsTokens, r => Assert.Contains(r.Facet, facets));

        // And all three are actually populated - a facet with no rules would make MatchEditions
        // quietly incapable of ever returning that kind of badge.
        Assert.All(facets, f => Assert.Contains(EditionCatalog.Editions, r => r.Facet == f));
    }

    /// <summary>
    /// The badge that a facet contributes must be unique to it, otherwise the same label could be
    /// drawn twice on one poster.
    /// </summary>
    [Fact]
    public void NoBadgeLabelAppearsInTwoFacets()
    {
        var offenders = EditionCatalog.Editions
            .Concat(EditionCatalog.EditionCapsTokens.Select(c => new EditionCatalog.Rule(c.Facet, c.Badge, c.Token)))
            .GroupBy(r => r.Badge)
            .Where(g => g.Select(r => r.Facet).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// An episode file gets the same treatment, since it shares the matcher.
    /// </summary>
    [Fact]
    public void AnEpisodeFileAlsoGetsBothBadges()
    {
        var result = EpisodeFileNameParser.Parse(
            "Show.S01E01.Uncut.Remastered.German.DL.1080p.BluRay.x264-GRP.mkv", null);

        Assert.Equal(new[] { "UC", "REM" }, result.Editions);
    }
}
