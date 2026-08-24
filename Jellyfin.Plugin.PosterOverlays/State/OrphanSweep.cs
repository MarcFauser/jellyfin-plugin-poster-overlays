using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.PosterOverlays.State;

/// <summary>
/// Decides which records describe items that no longer exist.
/// </summary>
/// <remarks>
/// Split from the task that runs it because the decision is arithmetic on a list of ids and the
/// lookup is the only part that needs a library. Kept apart, the refusal rules below can be made
/// to fail on demand, which is the only way to know they can fail at all.
/// </remarks>
internal static class OrphanSweep
{
    /// <summary>
    /// Works out what may safely be forgotten.
    /// </summary>
    /// <remarks>
    /// <b>The asymmetry is the whole design.</b> Keeping a dead record wastes about half a
    /// megabyte. Dropping a live one destroys the only copy of that item's unbadged cover, so the
    /// item keeps its badged image forever and the next run caches that as its "original" and
    /// draws on top of it. Badges stacking on badges is not self-correcting.
    /// <para>
    /// So the plan refuses on anything that looks less like deletion and more like a library that
    /// is not answering: no item found at all, or more than half the records unmatched. Those two
    /// are not a tuned threshold, they are the two shapes a lookup failure takes - total, and
    /// broad. An ordinary night deletes a handful.
    /// </para>
    /// </remarks>
    /// <param name="ids">Every id the store knows.</param>
    /// <param name="exists">Answers whether the library still holds an item.</param>
    /// <returns>The plan, never null.</returns>
    public static Plan Decide(IReadOnlyList<string> ids, Func<Guid, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(exists);

        var orphans = new List<string>();
        var unreadable = new List<string>();
        int alive = 0;

        foreach (string id in ids)
        {
            if (!Guid.TryParse(id, out var guid))
            {
                // Not written by this plugin's own key. Reported and left alone: an unexplained
                // record is a finding, and deleting it would take the evidence with it - along
                // with whatever cached original sits beside it.
                unreadable.Add(id);
                continue;
            }

            if (exists(guid))
            {
                alive++;
            }
            else
            {
                orphans.Add(id);
            }
        }

        bool refuse = orphans.Count > 0 && (alive == 0 || (orphans.Count * 2) > ids.Count);

        return new Plan(orphans, unreadable, alive, refuse);
    }

    /// <summary>
    /// What the sweep found and whether it dares act on it.
    /// </summary>
    /// <param name="Orphans">Records whose item the library does not know.</param>
    /// <param name="Unreadable">Record keys that are not item ids. Never removed.</param>
    /// <param name="Alive">How many records still have an item.</param>
    /// <param name="Refused">
    /// True when <see cref="Orphans"/> must not be acted on because the library looks wrong
    /// rather than smaller.
    /// </param>
    internal sealed record Plan(
        IReadOnlyList<string> Orphans,
        IReadOnlyList<string> Unreadable,
        int Alive,
        bool Refused);
}
