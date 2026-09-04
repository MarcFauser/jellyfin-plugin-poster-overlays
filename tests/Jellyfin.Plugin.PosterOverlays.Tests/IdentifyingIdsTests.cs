using System;
using System.Collections.Generic;
using Jellyfin.Plugin.PosterOverlays.Badges;
using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// Which provider ids may be used to decide that two rows are the same title.
/// </summary>
/// <remarks>
/// Written after the fault it prevents. The audio badge searched for peers by "any shared provider
/// id", which included <c>TmdbCollection</c> - an id every film of a series carries. All fifteen
/// Star Wars films became peers of one another, and a badge appeared on films with no second copy:
/// measured, 2 of 25 single-copy films drew one.
/// </remarks>
public class IdentifyingIdsTests
{
    /// <summary>
    /// A collection id groups titles and must never be used to identify one.
    /// </summary>
    /// <remarks>
    /// The real values, from the reference library: fifteen films share this collection.
    /// </remarks>
    [Fact]
    public void ACollectionIdIsNotAnIdentity()
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Imdb"] = "tt2527338",
            ["Tmdb"] = "181812",
            ["TmdbCollection"] = "10",
        };

        var kept = IdentifyingIds.From(ids);

        Assert.Equal(2, kept.Count);
        Assert.Equal("tt2527338", kept["Imdb"]);
        Assert.Equal("181812", kept["Tmdb"]);
        Assert.False(kept.ContainsKey("TmdbCollection"));
    }

    /// <summary>
    /// The four identifying keys are kept, whatever case they arrive in.
    /// </summary>
    /// <param name="key">The provider key.</param>
    [Theory]
    [InlineData("Imdb")]
    [InlineData("Tmdb")]
    [InlineData("Tvdb")]
    [InlineData("Custom")]
    [InlineData("tmdb")]
    [InlineData("IMDB")]
    public void IdentifyingKeysAreKept(string key)
    {
        var kept = IdentifyingIds.From(new Dictionary<string, string> { [key] = "x" });
        Assert.Single(kept);
    }

    /// <summary>
    /// Anything not on the list is dropped, because an unknown key might group rather than name.
    /// </summary>
    /// <param name="key">The provider key.</param>
    [Theory]
    [InlineData("TmdbCollection")]
    [InlineData("Zap2It")]
    [InlineData("TvRage")]
    [InlineData("MusicBrainzAlbum")]
    public void UnlistedKeysAreDropped(string key)
    {
        Assert.Empty(IdentifyingIds.From(new Dictionary<string, string> { [key] = "x" }));
    }

    /// <summary>
    /// An id with no value identifies nothing, and would match every item that has the key at all.
    /// </summary>
    /// <remarks>
    /// Jellyfin's own query treats an empty value as "has this provider set", which would be the
    /// same failure as the collection id: everything with a TMDB id would become a peer.
    /// </remarks>
    [Fact]
    public void EmptyValuesAreDropped()
    {
        var ids = new Dictionary<string, string> { ["Tmdb"] = string.Empty, ["Imdb"] = "  ", ["Tvdb"] = "42" };

        var kept = IdentifyingIds.From(ids);

        Assert.Single(kept);
        Assert.Equal("42", kept["Tvdb"]);
    }

    /// <summary>
    /// Nothing in, nothing out - and never null, so the caller can just count it.
    /// </summary>
    [Fact]
    public void NothingInNothingOut()
    {
        Assert.Empty(IdentifyingIds.From(null));
        Assert.Empty(IdentifyingIds.From(new Dictionary<string, string>()));
    }
}
