using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.PosterOverlays.Badges;

/// <summary>
/// Reads the edition and the source quality out of an episode's file name.
/// </summary>
/// <remarks>
/// Episodes need their own reader because a movie's release tags live in its folder name while an
/// episode's live in its file name: a flattened season is one folder called <c>Season 01</c> with
/// every episode beside each other in it, so the folder carries nothing. Measured on the library
/// this was built against, about 201 duplicate episodes sit in the <em>same</em> resolution, which
/// makes the technical badges read identically on both - the edition token in the file name is the
/// only thing that tells them apart:
/// <code>
/// buck.rogers.s01e01.german.dl.1080p.fs.bluray.x264-excited.mkv
/// buck.rogers.s01e01e02.german.dl.alternate.cut.1080p.bluray.x264-excited.mkv
/// </code>
/// <para>
/// <b>The <c>SxxExx</c> anchor is not on its own a guard against false positives.</b> The design
/// note that proposed this work called it a hard delimiter and treated everything after it as
/// release zone - that is a position rule, and <see cref="FolderNameParser"/> exists partly to
/// document why position rules fail here. Measured against the catalogue: of twelve plausible
/// episode titles, twelve fire a rule. <em>Final Cut</em> gives FIN, <em>Restored</em> gives REM,
/// <em>Recut</em> gives RC - and <em>The Extended Family</em>, an entirely ordinary title, gives
/// EXT, because the bare word <c>extended</c> is enough. Three harmless titles fired nothing,
/// which is the control that makes those twelve mean something.
/// </para>
/// <para>
/// So the anchor only says where the series title ends. What protects the result is the same title
/// subtraction the movie parser uses, applied to the episode title, plus one extra demand the
/// movie parser cannot make: the remaining zone has to contain at least one known release tag.
/// That is affordable here precisely <em>because</em> the anchor is hard - after it there is
/// nothing but the episode title and release tags, so a zone with no release tag in it is all
/// title and must be dropped whole. The movie parser widens its search when it distrusts the
/// title; this one gives up instead. A missing badge is a nuisance, a wrong one is a lie about
/// which copy you are looking at.
/// </para>
/// </remarks>
internal static class EpisodeFileNameParser
{
    /// <summary>
    /// One token carrying a whole marker: <c>s01e01</c>, <c>s01e01e02</c>, <c>s1e1</c>.
    /// </summary>
    private static readonly Regex CombinedMarker = new(
        @"^s[0-9]{1,2}(?:e[0-9]{1,3})+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The <c>1x01</c> spelling, which survives tokenising as one token because x is a letter.
    /// </summary>
    private static readonly Regex CrossMarker = new(
        @"^[0-9]{1,2}x[0-9]{1,3}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SeasonOnly = new(
        @"^s[0-9]{1,2}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EpisodeOnly = new(
        @"^e[0-9]{1,3}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Nothing found. <c>TitleTrusted</c> is false so a caller cannot mistake silence for a
    /// searched-and-empty zone.
    /// </summary>
    private static readonly FolderNameParser.Result Empty = new([], null, null, false, string.Empty);

    /// <summary>
    /// Parses an episode file name.
    /// </summary>
    /// <param name="path">The episode's full path, or just its file name. May be null.</param>
    /// <param name="episodeTitle">The episode title as Jellyfin knows it, or null.</param>
    /// <returns>The result, never null. Everything is null when no anchor was found or the zone
    /// after it does not look like release text.</returns>
    public static FolderNameParser.Result Parse(string? path, string? episodeTitle)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Empty;
        }

        // GetFileNameWithoutExtension rather than a hand-rolled split: a bare file name goes
        // through it unchanged, so the caller may hand over either.
        string name = Path.GetFileNameWithoutExtension(path);
        var tokens = FolderTokenizer.Tokenize(name);

        int afterAnchor = AnchorEnd(tokens);
        if (afterAnchor < 0)
        {
            return Empty;
        }

        // The series title sits in front of the anchor and is gone already. What can still be in
        // the way is the episode title, and that is subtracted exactly as the movie parser
        // subtracts the film title.
        int skip = afterAnchor + TitlePrefixLength(tokens, afterAnchor, episodeTitle);
        string zone = FolderTokenizer.NormalisedText(tokens, skip);

        if (!HasReleaseTag(tokens, skip))
        {
            return Empty;
        }

        return new FolderNameParser.Result(
            FolderNameParser.MatchEditions(zone, tokens, skip),
            FolderNameParser.MatchZone(zone, tokens, skip, EditionCatalog.Sources, EditionCatalog.SourceCapsTokens),
            FolderNameParser.MatchZone(zone, tokens, skip, EditionCatalog.Formats, EditionCatalog.FormatCapsTokens),
            TitleTrusted: true,
            zone);
    }

    /// <summary>
    /// Finds the index just past the episode marker.
    /// </summary>
    /// <remarks>
    /// The first marker wins, not the last: there is only ever one in a real name, and taking the
    /// first leaves the larger zone, which the title subtraction then trims. Trailing
    /// <c>E02</c> tokens are swallowed so that <c>S01E01-E02</c> ends where <c>S01E01E02</c> does.
    /// </remarks>
    /// <returns>The index of the first token after the marker, or -1.</returns>
    private static int AnchorEnd(IReadOnlyList<FolderTokenizer.Token> tokens)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            string t = tokens[i].Normalised;
            int end;

            if (CombinedMarker.IsMatch(t) || CrossMarker.IsMatch(t))
            {
                end = i + 1;
            }
            else if (SeasonOnly.IsMatch(t) && i + 1 < tokens.Count && EpisodeOnly.IsMatch(tokens[i + 1].Normalised))
            {
                end = i + 2;
            }
            else
            {
                continue;
            }

            while (end < tokens.Count && EpisodeOnly.IsMatch(tokens[end].Normalised))
            {
                end++;
            }

            return end;
        }

        return -1;
    }

    /// <summary>
    /// Counts how many tokens after the anchor the episode title covers.
    /// </summary>
    private static int TitlePrefixLength(IReadOnlyList<FolderTokenizer.Token> tokens, int start, string? title)
    {
        var titleTokens = FolderTokenizer.Tokenize(title);
        int i = 0;
        while (start + i < tokens.Count
               && i < titleTokens.Count
               && string.Equals(tokens[start + i].Normalised, titleTokens[i].Normalised, StringComparison.Ordinal))
        {
            i++;
        }

        return i;
    }

    /// <summary>
    /// Says whether what is left looks like release text rather than more title.
    /// </summary>
    private static bool HasReleaseTag(IReadOnlyList<FolderTokenizer.Token> tokens, int skip)
    {
        for (int i = skip; i < tokens.Count; i++)
        {
            if (EditionCatalog.ReleaseTags.Contains(tokens[i].Normalised))
            {
                return true;
            }
        }

        return false;
    }
}
