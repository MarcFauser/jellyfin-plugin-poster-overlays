using System;
using System.IO;

namespace Jellyfin.Plugin.PosterOverlays.State;

/// <summary>
/// Replaces a file so that a reader - or a machine coming back from a power cut - finds either the
/// old content or the new one, never something in between.
/// </summary>
/// <remarks>
/// <b>Why this exists: the state file was found empty on the reference server.</b>
/// <c>File.WriteAllText</c> opens with truncation and then writes, so for a moment the file is
/// zero bytes long - and with the operating system's write-back cache, "a moment" on disk lasts
/// until the kernel gets round to flushing the new content. The Jellyfin VM there was killed hard
/// by the host's out-of-memory killer on five nights in a row (2026-09-23 to 09-27), each time a
/// few minutes after the nightly run starts at 03:00, and by 09-27 03:00 <c>state.json</c> was
/// exactly 0 bytes. Measured, not assumed: the reader's message was "no JSON tokens" at line 0,
/// byte 0, which only an empty input produces - whitespace, NUL bytes and cut-off JSON each give a
/// different one. Which of the kills did it cannot be told any more; the server keeps four days of
/// logs.
/// <para>
/// So the bytes go to a sibling file first, are forced to the disk, and only then take the place
/// of the old file by a rename. A rename within one directory is atomic on every file system
/// Jellyfin runs on; <c>File.Move</c> with <c>overwrite</c> is <c>rename(2)</c> on Linux and
/// <c>MoveFileEx</c> with <c>MOVEFILE_REPLACE_EXISTING</c> on Windows.
/// </para>
/// <para>
/// What this does not promise: that the <i>new</i> version survives a crash in the instant after
/// the rename. The directory entry is not flushed separately - .NET offers no portable way to do
/// that - so a crash right then may bring back the old version. That is the harmless direction:
/// the old version is complete, and the callers are written so that one missing step is
/// recognised on the next run rather than mistaken for something else.
/// </para>
/// </remarks>
internal static class AtomicFile
{
    /// <summary>
    /// The suffix of the file the new content is written to first.
    /// </summary>
    /// <remarks>
    /// Fixed rather than random, so a leftover from a crash is overwritten by the next write
    /// instead of piling up. Anything that lists a folder written this way has to skip it.
    /// </remarks>
    public const string PendingSuffix = ".tmp";

    /// <summary>
    /// Writes <paramref name="bytes"/> to <paramref name="path"/> atomically.
    /// </summary>
    /// <param name="path">The file to replace or create.</param>
    /// <param name="bytes">The complete new content.</param>
    public static void Write(string path, ReadOnlySpan<byte> bytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string pending = path + PendingSuffix;
        using (var stream = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);

            // flushToDisk: true is the line the whole class is for. Without it the rename can reach
            // the disk before the data does, and a crash then leaves the new name on an empty file -
            // the very failure this replaces, only moved one step along.
            stream.Flush(flushToDisk: true);
        }

        File.Move(pending, path, overwrite: true);
    }
}
