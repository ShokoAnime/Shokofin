using System;
using System.Collections.Generic;
using MediaBrowser.Common.Providers;

#nullable enable
namespace Shokofin;

public static class StringExtensions
{
    public static void Deconstruct(this IList<string> list, out string first)
    {
        first = list.Count > 0 ? list[0] : "";
    }

    public static void Deconstruct(this IList<string> list, out string first, out string second)
    {
        first = list.Count > 0 ? list[0] : "";
        second = list.Count > 1 ? list[1] : "";
    }

    public static void Deconstruct(this IList<string> list, out string first, out string second, out string third)
    {
        first = list.Count > 0 ? list[0] : "";
        second = list.Count > 1 ? list[1] : "";
        third = list.Count > 2 ? list[2] : "";
    }

    public static void Deconstruct(this IList<string> list, out string first, out string second, out string third, out string forth)
    {
        first = list.Count > 0 ? list[0] : "";
        second = list.Count > 1 ? list[1] : "";
        third = list.Count > 2 ? list[2] : "";
        forth = list.Count > 3 ? list[3] : "";
    }

    public static void Deconstruct(this IList<string> list, out string first, out string second, out string third, out string forth, out string fifth)
    {
        first = list.Count > 0 ? list[0] : "";
        second = list.Count > 1 ? list[1] : "";
        third = list.Count > 2 ? list[2] : "";
        forth = list.Count > 3 ? list[3] : "";
        fifth = list.Count > 4 ? list[4] : "";
    }

    public static string Join(this IEnumerable<string> list, char separator)
        => string.Join(separator, list);

    public static string Join(this IEnumerable<string> list, string? separator)
        => string.Join(separator, list);

    public static string Join(this IEnumerable<string> list, char separator, int startIndex, int count)
        => string.Join(separator, list, startIndex, count);

    public static string Join(this IEnumerable<string> list, string? separator, int startIndex, int count)
        => string.Join(separator, list, startIndex, count);

    public static string ReplaceInvalidPathCharacters(this string path)
        => path
            .Replace(@"*", "\u1F7AF") // 🞯 (LIGHT FIVE SPOKED ASTERISK)
            .Replace(@"|", "\uFF5C") // ｜ (FULLWIDTH VERTICAL LINE)
            .Replace(@"\", "\u29F9") // ⧹ (BIG REVERSE SOLIDUS)
            .Replace(@"/", "\u29F8") // ⧸ (BIG SOLIDUS)
            .Replace(@":", "\u0589") // ։ (ARMENIAN FULL STOP)
            .Replace("\"", "\u2033") // ″ (DOUBLE PRIME)
            .Replace(@">", "\u203a") // › (SINGLE RIGHT-POINTING ANGLE QUOTATION MARK)
            .Replace(@"<", "\u2039") // ‹ (SINGLE LEFT-POINTING ANGLE QUOTATION MARK)
            .Replace(@"?", "\uff1f") // ？ (FULL WIDTH QUESTION MARK)
            .Replace(@".", "\u2024") // ․ (ONE DOT LEADER)
            .Trim();
    
    /// <summary>
    /// Gets the attribute value for <paramref name="attribute"/> in <paramref name="text"/>.
    /// </summary>
    /// <remarks>
    /// Borrowed and adapted from the following URL, since the extension is not exposed to the plugins.
    /// https://github.com/jellyfin/jellyfin/blob/25abe479ebe54a341baa72fd07e7d37cefe21a20/Emby.Server.Implementations/Library/PathExtensions.cs#L19-L62
    /// </remarks>
    /// <param name="text">The string to extract the attribute value from.</param>
    /// <param name="attribute">The attribibute name to extract.</param>
    /// <returns>The extracted attribute value, or null.</returns>
    /// <exception cref="ArgumentException"><paramref name="text" /> or <paramref name="attribute" /> is empty.</exception>
    public static string? GetAttributeValue(this string text, string attribute)
    {
        if (text.Length == 0)
            throw new ArgumentException("String can't be empty.", nameof(text));

        if (attribute.Length == 0)
            throw new ArgumentException("String can't be empty.", nameof(attribute));

        // Must be at least 3 characters after the attribute =, ], any character.
        var attributeIndex = text.IndexOf(attribute, StringComparison.OrdinalIgnoreCase);
        var maxIndex = text.Length - attribute.Length - 3;
        while (attributeIndex > -1 && attributeIndex < maxIndex)
        {
            var attributeEnd = attributeIndex + attribute.Length;
            if (
                attributeIndex > 0 &&
                text[attributeIndex - 1] == '[' &&
                (text[attributeEnd] == '=' || text[attributeEnd] == '-')
            ) {
                // Must be at least 1 character before the closing bracket.
                var closingIndex = text[attributeEnd..].IndexOf(']');
                if (closingIndex > 1)
                    return text[(attributeEnd + 1)..(attributeEnd + closingIndex)].Trim().ToString();
            }

            text = text[attributeEnd..];
            attributeIndex = text.IndexOf(attribute, StringComparison.OrdinalIgnoreCase);
        }

        // for IMDb we also accept pattern matching
        if (
            attribute.Equals("imdbid", StringComparison.OrdinalIgnoreCase) &&
            ProviderIdParsers.TryFindImdbId(text, out var imdbId)
        )
                return imdbId.ToString();

        return null;
    }
}