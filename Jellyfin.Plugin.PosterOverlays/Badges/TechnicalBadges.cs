using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.PosterOverlays.Badges;

/// <summary>
/// Derives the resolution and video range badges from the video stream.
/// </summary>
/// <remarks>
/// The stream is the source, not the folder name. Measured on a library of 2378 movies: the
/// streams report 288 entries with Dolby Vision or HDR, the folder names only 229, and one
/// 1080p entry carries <c>UHD.BluRay.DV.HDR</c> in its name. The name is a cross-check whose
/// disagreements are worth reporting, never the input.
/// </remarks>
internal static class TechnicalBadges
{
    /// <summary>
    /// Width of one "K" step. 3840 / 960 = 4, 7680 / 960 = 8, 15360 / 960 = 16.
    /// </summary>
    private const double WidthPerK = 960.0;

    /// <summary>
    /// The audio formats, best first.
    /// </summary>
    /// <remarks>
    /// <b>The order carries meaning twice over</b>: it is the quality ranking, and it decides
    /// which of two overlapping names wins. Atmos travels inside a TrueHD or E-AC-3 stream, and
    /// DTS:X inside DTS-HD, so the specific ones have to be tested before their containers - test
    /// "dts" first and no DTS:X track would ever be recognised as one.
    /// <para>
    /// "dts-es" sits with DTS-HD rather than with plain DTS because it is the extended-surround
    /// variant, and the two entries of Evangelion 2.0 on the reference library differ by exactly
    /// that.
    /// </para>
    /// </remarks>
    private static readonly (string[] Needles, string Label)[] Formats =
    [
        (["atmos"], "ATMOS"),
        (["dts:x", "dts-x", "dtsx"], "DTS-X"),
        (["truehd", "true-hd"], "TRUEHD"),
        (["dts-hd", "dts hd", "dtshd", "dts-es"], "DTS-HD"),
        (["dts"], "DTS"),
        (["flac"], "FLAC"),
        (["eac3", "e-ac-3"], "EAC3"),
        (["ac3", "ac-3"], "AC3"),
        (["opus"], "OPUS"),
        (["aac"], "AAC"),
    ];

    /// <summary>
    /// Turns a pixel width into a resolution badge.
    /// </summary>
    /// <param name="width">Width of the video stream in pixels. The width, not the height:
    /// a scope film measures 3840x1608, so the height is cropped while the width stays nominal.</param>
    /// <param name="ladder">Comma separated list of the K values that may appear.</param>
    /// <param name="minimumK">The lowest ladder entry that still earns a badge.</param>
    /// <returns>For example "4K", or null when the width is below the minimum.</returns>
    public static string? Resolution(int width, string? ladder, int minimumK)
    {
        if (width <= 0)
        {
            return null;
        }

        double raw = width / WidthPerK;

        // Half a step of tolerance, so that DCI 4K (4096) and a slightly trimmed master both
        // still reach the 4K rung without a hard-coded list of exceptions.
        if (raw < minimumK - 0.5)
        {
            return null;
        }

        int best = 0;
        double bestDistance = double.MaxValue;
        foreach (int rung in ParseLadder(ladder))
        {
            double distance = Math.Abs(rung - raw);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = rung;
            }
        }

        if (best < minimumK)
        {
            return null;
        }

        return best.ToString(CultureInfo.InvariantCulture) + "K";
    }

    /// <summary>
    /// Turns a <c>VideoRangeType</c> into badges.
    /// </summary>
    /// <param name="videoRangeType">The enum value as text, for example <c>DOVIWithHDR10</c>.
    /// Taken as text on purpose: the enum has thirteen members today and the plugin should not
    /// need a new build when a fourteenth appears.</param>
    /// <param name="merge">When true, Dolby Vision and HDR share one pill.</param>
    /// <returns>Zero, one or two labels.</returns>
    public static IReadOnlyList<string> VideoRange(string? videoRangeType, bool merge)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(videoRangeType))
        {
            return result;
        }

        string value = videoRangeType.Trim();
        if (value.Contains("Invalid", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "SDR", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        bool dolbyVision = value.StartsWith("DOVI", StringComparison.OrdinalIgnoreCase);

        string? hdr = null;
        if (value.Contains("HDR10Plus", StringComparison.OrdinalIgnoreCase))
        {
            hdr = "HDR+";
        }
        else if (value.Contains("HDR10", StringComparison.OrdinalIgnoreCase))
        {
            hdr = "HDR";
        }
        else if (value.Contains("HLG", StringComparison.OrdinalIgnoreCase))
        {
            hdr = "HLG";
        }

        if (dolbyVision && hdr is not null && merge)
        {
            result.Add("DV " + hdr);
            return result;
        }

        if (dolbyVision)
        {
            result.Add("DV");
        }

        if (hdr is not null)
        {
            result.Add(hdr);
        }

        return result;
    }

    /// <summary>
    /// The best audio format an item carries, as a short label.
    /// </summary>
    /// <remarks>
    /// <b>Only the best one.</b> A film with Atmos, DTS-HD and a stereo commentary is an Atmos
    /// film; listing all three would fill the poster with things that are true and useless.
    /// <para>
    /// The order below is the order of the formats' own hierarchy, and it is read from three
    /// fields rather than one: Jellyfin reports Atmos in the profile or the track title, never in
    /// the codec, which stays <c>eac3</c> or <c>truehd</c>. Measured on the reference library -
    /// searching only the codec finds no Atmos at all.
    /// </para>
    /// <para>
    /// <paramref name="withChannels"/> exists because a label is only worth drawing when it tells
    /// two copies apart, and sometimes the format alone does not: measured, two entries of
    /// Evangelion 2.0 are both plain DTS and differ only in 5.1 against 6.1. The caller asks for
    /// the coarse label first and comes back for this one when the coarse labels turn out equal.
    /// </para>
    /// </remarks>
    /// <param name="tracks">The audio tracks of the item.</param>
    /// <param name="withChannels">Append the channel layout, as in "DTS 7.1".</param>
    /// <returns>For example "ATMOS", or null when there is nothing worth saying.</returns>
    public static string? Audio(IReadOnlyList<AudioTrack> tracks, bool withChannels)
    {
        if (tracks is null || tracks.Count == 0)
        {
            return null;
        }

        string? best = null;
        int bestRank = int.MaxValue;
        int bestChannels = 0;

        foreach (var track in tracks)
        {
            string haystack = string.Join(
                ' ',
                track.Codec ?? string.Empty,
                track.Profile ?? string.Empty,
                track.Title ?? string.Empty);

            (string Label, int Rank)? found = Classify(haystack);
            if (found is null)
            {
                continue;
            }

            if (found.Value.Rank < bestRank)
            {
                bestRank = found.Value.Rank;
                best = found.Value.Label;
                bestChannels = track.Channels ?? 0;
            }
        }

        if (best is null)
        {
            return null;
        }

        if (!withChannels || bestChannels <= 0)
        {
            return best;
        }

        return best + " " + Channels(bestChannels);
    }

    /// <summary>
    /// Recognises one track's format.
    /// </summary>
    /// <param name="haystack">Codec, profile and title joined.</param>
    /// <returns>The label and its rank, or null when nothing matched.</returns>
    private static (string Label, int Rank)? Classify(string haystack)
    {
        for (int rank = 0; rank < Formats.Length; rank++)
        {
            foreach (string needle in Formats[rank].Needles)
            {
                if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    return (Formats[rank].Label, rank);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Turns a channel count into the spoken layout.
    /// </summary>
    /// <remarks>
    /// One of the counts is subtracted for the low frequency channel from six upwards, which is
    /// how these layouts are named everywhere: six channels are "5.1", not "6.0". Below six there
    /// is no LFE to account for.
    /// </remarks>
    /// <param name="channels">The channel count from the stream.</param>
    /// <returns>For example "5.1".</returns>
    private static string Channels(int channels) => channels switch
    {
        1 => "MONO",
        2 => "2.0",
        _ => (channels - 1).ToString(CultureInfo.InvariantCulture) + ".1",
    };

    private static IEnumerable<int> ParseLadder(string? ladder)
    {
        if (string.IsNullOrWhiteSpace(ladder))
        {
            yield return 4;
            yield return 8;
            yield break;
        }

        foreach (string part in ladder.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rung) && rung > 0)
            {
                yield return rung;
            }
        }
    }
}
