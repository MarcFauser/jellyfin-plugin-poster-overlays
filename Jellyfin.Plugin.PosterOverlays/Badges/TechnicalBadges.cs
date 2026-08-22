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
