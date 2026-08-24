using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.PosterOverlays.State;
using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// Deciding which records to forget.
/// </summary>
/// <remarks>
/// The refusal rules carry more weight than the collection rule, so they get the awkward cases.
/// Keeping a dead record costs disk; dropping a live one destroys the only unbadged copy of that
/// item's cover and the next run then draws a badge on top of a badge.
/// </remarks>
public class OrphanSweepTests
{
    private static List<string> Ids(int count) =>
        Enumerable.Range(0, count).Select(_ => Guid.NewGuid().ToString("N")).ToList();

    [Fact]
    public void TheOrdinaryCaseCollectsExactlyTheMissingOnes()
    {
        var ids = Ids(10);
        var gone = new HashSet<string>(new[] { ids[2], ids[7] }, StringComparer.OrdinalIgnoreCase);

        var plan = OrphanSweep.Decide(ids, guid => !gone.Contains(guid.ToString("N")));

        Assert.False(plan.Refused);
        Assert.Equal(8, plan.Alive);
        Assert.Equal(new[] { ids[2], ids[7] }, plan.Orphans);
    }

    [Fact]
    public void NothingMissingMeansNothingToDo()
    {
        var plan = OrphanSweep.Decide(Ids(5), _ => true);

        Assert.Empty(plan.Orphans);
        Assert.False(plan.Refused);
        Assert.Equal(5, plan.Alive);
    }

    /// <summary>
    /// The library answering "no" to everything is not a library that shrank to nothing, it is a
    /// library that is not answering.
    /// </summary>
    [Fact]
    public void ALibraryThatKnowsNothingIsRefused()
    {
        var plan = OrphanSweep.Decide(Ids(40), _ => false);

        Assert.True(plan.Refused);
        Assert.Equal(40, plan.Orphans.Count);
        Assert.Equal(0, plan.Alive);
    }

    /// <summary>
    /// Half is allowed, past half is not - and both sides are asserted, because a rule tested only
    /// on the side that trips it does not show where it stops.
    /// </summary>
    [Theory]
    [InlineData(10, 5, false)]
    [InlineData(10, 6, true)]
    [InlineData(3, 1, false)]
    [InlineData(3, 2, true)]
    public void PastHalfIsRefused(int total, int missing, bool expectRefused)
    {
        var ids = Ids(total);
        var gone = new HashSet<string>(ids.Take(missing), StringComparer.OrdinalIgnoreCase);

        var plan = OrphanSweep.Decide(ids, guid => !gone.Contains(guid.ToString("N")));

        Assert.Equal(expectRefused, plan.Refused);
        Assert.Equal(missing, plan.Orphans.Count);
    }

    /// <summary>
    /// A key that is not an item id is reported and never collected. It cannot be judged, so
    /// deleting it would be a guess with an irreversible outcome.
    /// </summary>
    [Fact]
    public void AKeyThatIsNotAnItemIdIsNeverForgotten()
    {
        var ids = Ids(4);
        ids.Add("not-a-guid");

        var plan = OrphanSweep.Decide(ids, _ => true);

        Assert.Equal(new[] { "not-a-guid" }, plan.Unreadable);
        Assert.Empty(plan.Orphans);
        Assert.Equal(4, plan.Alive);
    }

    /// <summary>
    /// And an unreadable key does not count as alive either, so it cannot prop up the majority
    /// rule and let a refusal through.
    /// </summary>
    [Fact]
    public void AnUnreadableKeyDoesNotCountAsAlive()
    {
        var ids = Ids(2);
        ids.Add("not-a-guid");

        // Both real records are gone. Were the unreadable one counted as alive, alive would be 1
        // and 2 of 3 is past half - refused either way, so the discriminating case is this one:
        var plan = OrphanSweep.Decide(ids, _ => false);

        Assert.Equal(0, plan.Alive);
        Assert.True(plan.Refused);
    }

    /// <summary>
    /// The lookup is called once per id and not more, because on a large library it is the
    /// expensive part.
    /// </summary>
    [Fact]
    public void EachIdIsLookedUpExactlyOnce()
    {
        var ids = Ids(25);
        var seen = new List<Guid>();

        OrphanSweep.Decide(ids, guid => { seen.Add(guid); return true; });

        Assert.Equal(25, seen.Count);
        Assert.Equal(25, seen.Distinct().Count());
    }
}
