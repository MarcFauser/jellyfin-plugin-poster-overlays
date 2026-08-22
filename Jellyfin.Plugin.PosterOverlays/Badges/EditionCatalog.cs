using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.PosterOverlays.Badges;

/// <summary>
/// The edition vocabulary and the source-quality vocabulary, in priority order.
/// </summary>
/// <remarks>
/// Compiled from six independently checked sources (Radarr's EditionRegex, the scene rules
/// 17.2, Kometa's edition overlays, the Jellyfin documentation, German release naming, the
/// Jellyfin server source), then trimmed against a real library of 2378 movie folders. Rows
/// with zero occurrences in that library are kept on purpose - they cost nothing and the next
/// release that carries one of them is badged correctly on the first run.
/// <para>
/// Rejected on purpose, each for a reason: NR and "not rated" (a classification, not a cut),
/// OV and OmU (a soundtrack, same cut), bare EE and bare TV (unattested, and TV is the scene's
/// TV-movie distribution tag), supercut (no attestation but several real film titles),
/// Criterion and [CC] (a distributor label; CC also means closed captions), Diamond, Platinum,
/// Deluxe and Definitive (disc marketing tiers whose stems collide with real titles), and the
/// generic "&lt;word&gt; Cut" fallback, which cannot be told apart from a title.
/// </para>
/// </remarks>
internal static class EditionCatalog
{
    /// <summary>
    /// Gets the edition rules, highest priority first. The first match wins and exactly one
    /// edition badge is drawn.
    /// </summary>
    /// <remarks>
    /// Combinations come before singles so that "Extended Directors Cut" never degrades to
    /// EXT or DC.
    /// </remarks>
    public static IReadOnlyList<Rule> Editions { get; } = new[]
    {
        // combinations
        new Rule("EDC",  @"extended directors cut|recut directors cut"),
        new Rule("UEC",  @"unrated extended (?:cut|edition|version)|extended unrated"),
        new Rule("UEE",  @"ultimate extended (?:cut|edition)"),

        // named cuts - unambiguous, so they may stand above the generic ones
        new Rule("DON",  @"(?:richard )?donner cut"),
        new Rule("SNY",  @"snyder cut"),
        new Rule("RGC",  @"ro[gu]e cut"),
        new Rule("ASC",  @"assembly cut"),
        new Rule("PRC",  @"producers cut"),
        new Rule("DESP", @"despecial[iy][sz]ed"),

        // generic cuts
        new Rule("DC",   @"directors? cut|directors (?:edition|version)|regiefassung"),
        new Rule("EXT",  @"extended(?: (?:cut|edition|version))?|langfassung|lange fassung"),
        new Rule("ULT",  @"ultimate (?:cut|edition|version)"),
        new Rule("FIN",  @"final (?:cut|edition|version)"),
        new Rule("RDX",  @"redux"),
        new Rule("RC",   @"recut"),
        new Rule("FAN",  @"fan ?edit(?:ion)?"),
        new Rule("INT",  @"international (?:cut|version)|internationale fassung"),

        // framing and presentation
        new Rule("IMAX", @"(?<!non )imax(?: (?:enhanced|edition|version))?"),
        new Rule("OM",   @"open ?matte"),
        new Rule("THR",  @"theatrical(?: (?:cut|edition|version))?|kinofassung|kinoversion"),

        // content
        new Rule("UC",   @"uncut|ungeschnitten|ungekuerzt"),
        new Rule("UNZ",  @"uncensored|unzensiert(?:e fassung)?"),
        new Rule("UR",   @"unrated(?: (?:cut|edition|version))?"),
        new Rule("BW",   @"black and chrome|black chrome|logan noir|justice is gray|minus colou?r"),
        new Rule("COL",  @"colou?rized|kolorierte fassung"),
        new Rule("ALT",  @"alternat(?:e|ive) (?:cut|ending|version)"),
        new Rule("2IN1", @"[234]in1"),

        // packaging that still tells two folders apart
        new Rule("SE",   @"special (?:edition|cut)|sonderedition"),
        new Rule("CE",   @"collectors (?:edition|cut)|sammleredition"),
        new Rule("AE",   @"anniversary edition|[0-9]{1,3}(?:st|nd|rd|th) anniversary|jubilaeumsedition"),
        new Rule("REM",  @"remastere?d?|restored|restauriert(?:e fassung)?|4k restoration"),
        new Rule("TVF",  @"tv (?:cut|version|fassung)|deutsche tv fassung|fernsehfassung"),
    };

    /// <summary>
    /// Gets the short edition tokens that need the capitals rule. They are tested after every
    /// spelled-out rule has failed.
    /// </summary>
    /// <remarks>
    /// Measured: all six folders in the reference library carrying a bare <c>DC</c> really are
    /// director's cuts, while "DC League of Super-Pets" carries it in the title - which the
    /// title subtraction removes before this rule ever sees it. The capitals requirement is the
    /// second guard, for the case where no metadata title is available.
    /// </remarks>
    public static IReadOnlyList<CapsRule> EditionCapsTokens { get; } = new[]
    {
        new CapsRule("DC", "DC"),
        new CapsRule("SE", "SE"),
        new CapsRule("OM", "OM"),
        new CapsRule("BW", "BW"),
        new CapsRule("CHR", "CHRONO"),
    };

    /// <summary>
    /// Gets the source-quality rules. A hit here means the copy is a placeholder rip.
    /// </summary>
    public static IReadOnlyList<Rule> Sources { get; } = new[]
    {
        new Rule("CAM", @"camrip|hdcam"),
        new Rule("TS",  @"telesync|hdts"),
        new Rule("TC",  @"telecine|hdtc"),
        new Rule("SCR", @"screener|dvdscr|bdscr"),
        new Rule("WP",  @"workprint"),
    };

    /// <summary>
    /// Gets the short source tokens that need the capitals rule. <c>CAM</c> is in here rather
    /// than in the spelled-out list because "Cam" is a real film title.
    /// </summary>
    public static IReadOnlyList<CapsRule> SourceCapsTokens { get; } = new[]
    {
        new CapsRule("CAM", "CAM"),
        new CapsRule("TS", "TS"),
        new CapsRule("TC", "TC"),
        new CapsRule("SCR", "SCR"),
        new CapsRule("WP", "WP"),
    };

    /// <summary>
    /// Gets the release tags used to decide whether a tag zone looks like a release name at
    /// all. Only used for that decision, never for matching.
    /// </summary>
    public static IReadOnlySet<string> ReleaseTags { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "german", "english", "dl", "ml", "ld", "md", "dubbed", "subbed", "hc",
        "dts", "dtshd", "dtsd", "ac3", "ac3d", "eac3", "eac3d", "truehd", "atmos", "aac", "flac",
        "x264", "x265", "h264", "h265", "xvid", "avc", "hevc", "10bit",
        "bluray", "bdrip", "brrip", "webrip", "web", "webdl", "hdtv", "dvdrip", "remux", "uhd",
        "hdr", "hdr10", "hdr10plus", "dv", "dovi", "sdr", "hlg",
        "1080p", "1080i", "720p", "576p", "480p", "2160p", "4k",
        "complete", "doku", "docu", "anime", "internal", "readnfo", "proper", "repack", "rerip",
        "hybrid", "limited", "retail", "custom", "nf", "amzn", "dsnp", "itunes", "atvp", "hmax",
    };

    /// <summary>
    /// A vocabulary entry.
    /// </summary>
    /// <param name="Badge">The label to draw.</param>
    /// <param name="Pattern">Alternation matched against the normalised tag zone.</param>
    internal sealed record Rule(string Badge, string Pattern)
    {
        private Regex? _compiled;

        /// <summary>
        /// Tests the rule against a normalised tag zone.
        /// </summary>
        /// <param name="zone">Normalised, space separated text.</param>
        /// <returns>True when the rule matches.</returns>
        public bool Matches(string zone)
        {
            _compiled ??= new Regex(
                "(^| )(?:" + Pattern + ")( |$)",
                RegexOptions.CultureInvariant | RegexOptions.Compiled);
            return _compiled.IsMatch(zone);
        }
    }

    /// <summary>
    /// A short token that is only accepted when the folder spells it in capitals.
    /// </summary>
    /// <param name="Badge">The label to draw.</param>
    /// <param name="Token">The exact upper case spelling in the folder name.</param>
    internal sealed record CapsRule(string Badge, string Token);
}
