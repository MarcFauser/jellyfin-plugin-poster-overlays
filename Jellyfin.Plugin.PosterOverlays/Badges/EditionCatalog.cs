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
    /// Gets the edition rules, highest priority first. Within one <see cref="EditionFacet"/> the
    /// first match wins; across facets one badge each may be drawn.
    /// </summary>
    /// <remarks>
    /// Combinations come before singles so that "Extended Directors Cut" never degrades to
    /// EXT or DC.
    /// <para>
    /// The facet is what lets a folder carry two edition badges without producing nonsense.
    /// "Extended Remastered" says two independent things - which cut, and how it was mastered -
    /// while "Extended Directors Cut" says one thing twice, and only the first kind may be drawn
    /// side by side. Measured on the reference library: of 2380 films exactly six carry two
    /// tokens, in three shapes - REM+UC four times, OM+UC once, EXT+REM once - and all three are
    /// a cut plus something that is not a cut. There is no attested pair within one facet, which
    /// is why "one per facet" is a rule about meaning and not a cap picked to fit the data.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Rule> Editions { get; } = new[]
    {
        // combinations
        new Rule(EditionFacet.Cut, "EDC",  @"extended directors cut|recut directors cut"),
        new Rule(EditionFacet.Cut, "UEC",  @"unrated extended (?:cut|edition|version)|extended unrated"),
        new Rule(EditionFacet.Cut, "UEE",  @"ultimate extended (?:cut|edition)"),

        // named cuts - unambiguous, so they may stand above the generic ones
        new Rule(EditionFacet.Cut, "DON",  @"(?:richard )?donner cut"),
        new Rule(EditionFacet.Cut, "SNY",  @"snyder cut"),
        new Rule(EditionFacet.Cut, "RGC",  @"ro[gu]e cut"),
        new Rule(EditionFacet.Cut, "ASC",  @"assembly cut"),
        new Rule(EditionFacet.Cut, "PRC",  @"producers cut"),
        new Rule(EditionFacet.Cut, "DESP", @"despecial[iy][sz]ed"),

        // generic cuts
        new Rule(EditionFacet.Cut, "DC",   @"directors? cut|directors (?:edition|version)|regiefassung"),
        new Rule(EditionFacet.Cut, "EXT",  @"extended(?: (?:cut|edition|version))?|langfassung|lange fassung"),
        new Rule(EditionFacet.Cut, "ULT",  @"ultimate (?:cut|edition|version)"),
        new Rule(EditionFacet.Cut, "FIN",  @"final (?:cut|edition|version)"),
        new Rule(EditionFacet.Cut, "RDX",  @"redux"),
        new Rule(EditionFacet.Cut, "RC",   @"recut"),
        new Rule(EditionFacet.Cut, "FAN",  @"fan ?edit(?:ion)?"),
        new Rule(EditionFacet.Cut, "INT",  @"international (?:cut|version)|internationale fassung"),

        // framing and presentation
        new Rule(EditionFacet.Presentation, "IMAX", @"(?<!non )imax(?: (?:enhanced|edition|version))?"),
        new Rule(EditionFacet.Presentation, "OM",   @"open ?matte"),
        // "kino ?fassung" rather than "kinofassung": the nightly run reported a folder spelling it
        // as two words, and the catalogue only knew the compound. The optional space costs nothing
        // and cannot widen the match - "kino" alone still matches no rule.
        new Rule(EditionFacet.Cut, "THR",  @"theatrical(?: (?:cut|edition|version))?|kino ?(?:fassung|version)"),

        // content
        new Rule(EditionFacet.Cut, "UC",   @"uncut|ungeschnitten|ungekuerzt"),
        new Rule(EditionFacet.Cut, "UNZ",  @"uncensored|unzensiert(?:e fassung)?"),
        new Rule(EditionFacet.Cut, "UR",   @"unrated(?: (?:cut|edition|version))?"),
        new Rule(EditionFacet.Presentation, "BW",   @"black and chrome|black chrome|logan noir|justice is gray|minus colou?r"),
        new Rule(EditionFacet.Presentation, "COL",  @"colou?rized|kolorierte fassung"),
        new Rule(EditionFacet.Cut, "ALT",  @"alternat(?:e|ive) (?:cut|ending|version)"),
        new Rule(EditionFacet.Cut, "2IN1", @"[234]in1"),

        // packaging that still tells two folders apart
        new Rule(EditionFacet.Cut, "SE",   @"special (?:edition|cut)|sonderedition"),
        new Rule(EditionFacet.Cut, "CE",   @"collectors (?:edition|cut)|sammleredition"),
        new Rule(EditionFacet.Cut, "AE",   @"anniversary edition|[0-9]{1,3}(?:st|nd|rd|th) anniversary|jubilaeumsedition"),
        new Rule(EditionFacet.Master, "REM",  @"remastere?d?|restored|restauriert(?:e fassung)?|4k restoration"),
        new Rule(EditionFacet.Cut, "TVF",  @"tv (?:cut|version|fassung)|deutsche tv fassung|fernsehfassung"),
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
        new CapsRule(EditionFacet.Cut, "DC", "DC"),
        new CapsRule(EditionFacet.Cut, "SE", "SE"),
        new CapsRule(EditionFacet.Presentation, "OM", "OM"),
        new CapsRule(EditionFacet.Presentation, "BW", "BW"),
        new CapsRule(EditionFacet.Cut, "CHR", "CHRONO"),
    };

    /// <summary>
    /// Gets the presentation-format rules.
    /// </summary>
    /// <remarks>
    /// Everything here means the same thing - the film is a 3D release - and the spellings are
    /// the ones the stereoscopic packing formats use: side-by-side, top-and-bottom, and the
    /// Blu-ray 3D codec MVC. <c>tab</c> is left out of the spelled-out list on purpose: it is a
    /// common word, so it is only accepted in capitals, further down.
    /// </remarks>
    public static IReadOnlyList<Rule> Formats { get; } = new[]
    {
        new Rule("3D", @"3d|half ?sbs|full ?sbs|sbs3d|hsbs|htab|mvc|anaglyph"),
    };

    /// <summary>
    /// Gets the short format tokens that need the capitals rule.
    /// </summary>
    public static IReadOnlyList<CapsRule> FormatCapsTokens { get; } = new[]
    {
        new CapsRule("3D", "SBS"),
        new CapsRule("3D", "TAB"),
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
    /// <param name="Facet">
    /// Which kind of statement this token makes. Only meaningful for <see cref="Editions"/> -
    /// formats and sources are single-valued and never grouped, so they use the constructor
    /// below and their facet is never read.
    /// </param>
    /// <param name="Badge">The label to draw.</param>
    /// <param name="Pattern">Alternation matched against the normalised tag zone.</param>
    internal sealed record Rule(EditionFacet Facet, string Badge, string Pattern)
    {
        private Regex? _compiled;

        /// <summary>
        /// Initializes a new instance of the <see cref="Rule"/> class for a vocabulary that has
        /// no facets.
        /// </summary>
        /// <param name="badge">The label to draw.</param>
        /// <param name="pattern">Alternation matched against the normalised tag zone.</param>
        public Rule(string badge, string pattern)
            : this(EditionFacet.Cut, badge, pattern)
        {
        }

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
    /// <param name="Facet">Which kind of statement this token makes.</param>
    /// <param name="Badge">The label to draw.</param>
    /// <param name="Token">The exact upper case spelling in the folder name.</param>
    internal sealed record CapsRule(EditionFacet Facet, string Badge, string Token)
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CapsRule"/> class for a vocabulary that
        /// has no facets.
        /// </summary>
        /// <param name="badge">The label to draw.</param>
        /// <param name="token">The exact upper case spelling in the folder name.</param>
        public CapsRule(string badge, string token)
            : this(EditionFacet.Cut, badge, token)
        {
        }
    }
}
