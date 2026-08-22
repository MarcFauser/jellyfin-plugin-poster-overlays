using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.PosterOverlays.Badges;

/// <summary>
/// Splits a folder name into tokens, keeping the original spelling next to a normalised one.
/// </summary>
/// <remarks>
/// Both forms come out of the same split, so token n of the raw list and token n of the
/// normalised list are always the same token. That alignment is what lets the parser apply a
/// rule to the normalised text and still ask "was it written in capitals?" - the check that
/// separates the release tag <c>DC</c> from a lower-case word.
/// </remarks>
internal static class FolderTokenizer
{
    /// <summary>
    /// Splits a name into aligned raw and normalised tokens.
    /// </summary>
    /// <param name="name">The folder name, or any other string.</param>
    /// <returns>The tokens, never null, possibly empty.</returns>
    public static IReadOnlyList<Token> Tokenize(string? name)
    {
        var tokens = new List<Token>();
        if (string.IsNullOrWhiteSpace(name))
        {
            return tokens;
        }

        // Apostrophes are removed before splitting, not treated as separators: "Director's"
        // has to stay one token so it can align with the normalised "directors".
        var current = new StringBuilder();
        foreach (char c in name)
        {
            if (c == '\'' || c == '’' || c == '´' || c == '`')
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                current.Append(c);
                continue;
            }

            if (current.Length > 0)
            {
                tokens.Add(Make(current.ToString()));
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(Make(current.ToString()));
        }

        return tokens;
    }

    /// <summary>
    /// Joins the normalised form of a token range into a single space separated string.
    /// </summary>
    /// <param name="tokens">The tokens.</param>
    /// <param name="skip">How many tokens to drop from the front.</param>
    /// <returns>The normalised text.</returns>
    public static string NormalisedText(IReadOnlyList<Token> tokens, int skip)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        var sb = new StringBuilder();
        for (int i = skip; i < tokens.Count; i++)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(tokens[i].Normalised);
        }

        return sb.ToString();
    }

    private static Token Make(string raw)
    {
        return new Token(raw, Normalise(raw));
    }

    private static string Normalise(string raw)
    {
        string lower = raw.ToLowerInvariant();

        // German umlauts first and explicitly: the generic accent stripping below would turn
        // "ü" into "u", but German folder names spell it "ue" when they avoid the umlaut, and
        // both spellings occur for the same film in this kind of library.
        var folded = new StringBuilder(lower.Length + 4);
        foreach (char c in lower)
        {
            switch (c)
            {
                case 'ä': folded.Append("ae"); break;
                case 'ö': folded.Append("oe"); break;
                case 'ü': folded.Append("ue"); break;
                case 'ß': folded.Append("ss"); break;
                default: folded.Append(c); break;
            }
        }

        string decomposed = folded.ToString().Normalize(NormalizationForm.FormD);
        var stripped = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                stripped.Append(c);
            }
        }

        return stripped.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// One token of a folder name.
    /// </summary>
    /// <param name="Raw">The token as written, case preserved.</param>
    /// <param name="Normalised">Lower case, umlauts folded to ae/oe/ue/ss, accents stripped.</param>
    internal sealed record Token(string Raw, string Normalised);
}
