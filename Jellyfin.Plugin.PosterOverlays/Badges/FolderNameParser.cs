using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.PosterOverlays.Badges;

/// <summary>
/// Reads the edition and the source quality out of a release folder name.
/// </summary>
/// <remarks>
/// The folder name is the only source. Not the file name, and above all not the item name:
/// Jellyfin's own <c>CleanStrings</c> strips <c>dc</c>, <c>se</c>, <c>unrated</c>, <c>4k</c>,
/// <c>hdr</c> and every bracketed suffix before a plugin ever sees a name.
/// <para>
/// The guard against false positives is title subtraction, not a position rule. Films actually
/// called <em>Uncut Gems</em>, <em>The Final Cut</em>, <em>Black and White</em> or
/// <em>Director's Cut</em> exist, and in all of them the token sits directly in front of the
/// year - exactly where a position rule would accept it. Subtracting the item title first
/// removes them, and it is the same trick Jellyfin uses in <c>IsEligibleForMultiVersion</c>.
/// </para>
/// <para>
/// Measured against 2378 real movie folders: 168 carry an edition token, 70 of them
/// <em>before</em> the year and 96 after, so restricting the search to the text after the year
/// would lose 43 per cent of them. 186 items have no metadata match at all - their name is the
/// folder name - which is why the trust check and the fallback below are not decoration.
/// </para>
/// </remarks>
internal static class FolderNameParser
{
    private static readonly Regex PureYear = new(@"^(?:19|20)[0-9]{2}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Parses a release folder name.
    /// </summary>
    /// <param name="folderName">The leaf folder name, for example
    /// <c>Avatar.Aufbruch.nach.Pandora.Extended.2009.German.DTS.1080p.BluRay.x264-SoW</c>.</param>
    /// <param name="itemName">The item name as Jellyfin knows it, or null.</param>
    /// <param name="originalTitle">The original title, or null. Tried when the name does not fit:
    /// a German library holds <em>Uncut Gems</em> as <em>Der schwarze Diamant</em>.</param>
    /// <returns>The result, never null.</returns>
    public static Result Parse(string? folderName, string? itemName, string? originalTitle)
    {
        var tokens = FolderTokenizer.Tokenize(folderName);
        if (tokens.Count == 0)
        {
            return new Result(null, null, false, string.Empty);
        }

        int skip = Math.Max(TitlePrefixLength(tokens, itemName), TitlePrefixLength(tokens, originalTitle));
        bool trusted = skip >= 1 && ZoneLooksLikeRelease(tokens, skip);
        if (!trusted)
        {
            // The title ate the whole folder name, so it is not a title at all - the item has
            // no metadata match and its name was taken from the folder. Searching the full
            // name is worse than searching a real tag zone, but it is the only thing left, and
            // it is still guarded by the capitals rule for short tokens.
            skip = 0;
        }

        string zone = FolderTokenizer.NormalisedText(tokens, skip);

        return new Result(
            Match(zone, tokens, skip, EditionCatalog.Editions, EditionCatalog.EditionCapsTokens),
            Match(zone, tokens, skip, EditionCatalog.Sources, EditionCatalog.SourceCapsTokens),
            trusted,
            zone);
    }

    private static string? Match(
        string zone,
        IReadOnlyList<FolderTokenizer.Token> tokens,
        int skip,
        IReadOnlyList<EditionCatalog.Rule> rules,
        IReadOnlyList<EditionCatalog.CapsRule> capsRules)
    {
        foreach (var rule in rules)
        {
            if (rule.Matches(zone))
            {
                return rule.Badge;
            }
        }

        foreach (var caps in capsRules)
        {
            for (int i = skip; i < tokens.Count; i++)
            {
                if (string.Equals(tokens[i].Raw, caps.Token, StringComparison.Ordinal))
                {
                    return caps.Badge;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Counts how many leading folder tokens the title covers.
    /// </summary>
    private static int TitlePrefixLength(IReadOnlyList<FolderTokenizer.Token> folder, string? title)
    {
        var titleTokens = FolderTokenizer.Tokenize(title);
        int i = 0;
        while (i < folder.Count
               && i < titleTokens.Count
               && string.Equals(folder[i].Normalised, titleTokens[i].Normalised, StringComparison.Ordinal))
        {
            i++;
        }

        return i;
    }

    /// <summary>
    /// Decides whether what is left after the title still looks like a release name.
    /// </summary>
    /// <remarks>
    /// A pure year token or a known release tag is enough. "Pure" matters: it is what keeps
    /// <c>x264-Mooi1990</c> from being read as a year, because that is one token and not four
    /// digits on their own.
    /// </remarks>
    private static bool ZoneLooksLikeRelease(IReadOnlyList<FolderTokenizer.Token> tokens, int skip)
    {
        for (int i = skip; i < tokens.Count; i++)
        {
            if (PureYear.IsMatch(tokens[i].Normalised)
                || EditionCatalog.ReleaseTags.Contains(tokens[i].Normalised))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What the parser found, plus what it had to work with.
    /// </summary>
    /// <param name="Edition">The edition badge, or null.</param>
    /// <param name="Source">The source-quality badge, or null.</param>
    /// <param name="TitleTrusted">
    /// False when the item title could not be subtracted and the whole folder name was searched.
    /// Worth logging: it means the item has no metadata match.
    /// </param>
    /// <param name="TagZone">The normalised text the rules were applied to.</param>
    internal sealed record Result(string? Edition, string? Source, bool TitleTrusted, string TagZone);
}
