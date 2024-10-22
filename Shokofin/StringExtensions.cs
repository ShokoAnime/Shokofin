using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using MediaBrowser.Common.Providers;
using Shokofin.ExternalIds;

namespace Shokofin;

public static partial class StringExtensions
{
    public static string Replace(this string input, Regex regex, string replacement, int count, int startAt)
        =>  regex.Replace(input, replacement, count, startAt);

    public static string Replace(this string input, Regex regex, MatchEvaluator evaluator, int count, int startAt)
        =>  regex.Replace(input, evaluator, count, startAt);

    public static string Replace(this string input, Regex regex, MatchEvaluator evaluator, int count)
        =>  regex.Replace(input, evaluator, count);

    public static string Replace(this string input, Regex regex, MatchEvaluator evaluator)
        =>  regex.Replace(input, evaluator);

    public static string Replace(this string input, Regex regex, string replacement)
        =>  regex.Replace(input, replacement);

    public static string Replace(this string input, Regex regex, string replacement, int count)
        =>  regex.Replace(input, replacement, count);

    public static void Deconstruct(this IList<string> list, out string first)
    {
        first = list.Count > 0 ? list[0] : string.Empty;
    }

    public static void Deconstruct(this IList<string> list, out string first, out string second)
    {
        first = list.Count > 0 ? list[0] : string.Empty;
        second = list.Count > 1 ? list[1] : string.Empty;
    }

    public static void Deconstruct(this IList<string> list, out string first, out string second, out string third)
    {
        first = list.Count > 0 ? list[0] : string.Empty;
        second = list.Count > 1 ? list[1] : string.Empty;
        third = list.Count > 2 ? list[2] : string.Empty;
    }

    public static void Deconstruct(this IList<string> list, out string first, out string second, out string third, out string forth)
    {
        first = list.Count > 0 ? list[0] : string.Empty;
        second = list.Count > 1 ? list[1] : string.Empty;
        third = list.Count > 2 ? list[2] : string.Empty;
        forth = list.Count > 3 ? list[3] : string.Empty;
    }

    public static void Deconstruct(this IList<string> list, out string first, out string second, out string third, out string forth, out string fifth)
    {
        first = list.Count > 0 ? list[0] : string.Empty;
        second = list.Count > 1 ? list[1] : string.Empty;
        third = list.Count > 2 ? list[2] : string.Empty;
        forth = list.Count > 3 ? list[3] : string.Empty;
        fifth = list.Count > 4 ? list[4] : string.Empty;
    }

    public static string Join(this IEnumerable<string> list, char separator)
        => string.Join(separator, list);

    public static string Join(this IEnumerable<string> list, string? separator)
        => string.Join(separator, list);

    public static string Join(this IEnumerable<string> list, char separator, int startIndex, int count)
        => string.Join(separator, list, startIndex, count);

    public static string Join(this IEnumerable<string> list, string? separator, int startIndex, int count)
        => string.Join(separator, list, startIndex, count);

    public static string Join(this IEnumerable<char> list, char separator)
        => string.Join(separator, list);

    public static string Join(this IEnumerable<char> list, string? separator)
        => string.Join(separator, list);

    public static string Join(this IEnumerable<char> list, char separator, int startIndex, int count)
        => string.Join(separator, list, startIndex, count);

    public static string Join(this IEnumerable<char> list, string? separator, int startIndex, int count)
        => string.Join(separator, list, startIndex, count);

    private static char? IsAllowedCharacter(this char c)
        => c == 32 || c > 47 && c < 58 || c > 64 && c < 91 || c > 96 && c < 123 ? c : '_';

    public static string ForceASCII(this string value)
        => value.Select(c => c.IsAllowedCharacter()).OfType<char>().Join("");

    private static string CompactUnderscore(this string path)
        => Regex.Replace(path, @"_{2,}", "_", RegexOptions.Singleline);

    public static string CompactWhitespaces(this string path)
        => Regex.Replace(path, @"\s{2,}", " ", RegexOptions.Singleline);

    public static string ReplaceInvalidPathCharacters(this string path)
        => path.ForceASCII().CompactUnderscore().CompactWhitespaces().Trim();

    /// <summary>
    /// Gets the attribute value for <paramref name="attribute"/> in <paramref name="text"/>.
    /// </summary>
    /// <remarks>
    /// Borrowed and adapted from the following URL, since the extension is not exposed to the plugins.
    /// https://github.com/jellyfin/jellyfin/blob/25abe479ebe54a341baa72fd07e7d37cefe21a20/Emby.Server.Implementations/Library/PathExtensions.cs#L19-L62
    /// </remarks>
    /// <param name="text">The string to extract the attribute value from.</param>
    /// <param name="attribute">The attribute name to extract.</param>
    /// <returns>The extracted attribute value, or null.</returns>
    /// <exception cref="ArgumentException"><paramref name="text" /> or <paramref name="attribute" /> is empty.</exception>
    public static string? GetAttributeValue(this string text, string attribute)
    {
        if (text.Length == 0)
            throw new ArgumentException("String can't be empty.", nameof(text));

        if (attribute.Length == 0)
            throw new ArgumentException("String can't be empty.", nameof(attribute));

        // Must be at least 3 characters after the attribute =, ], any character,
        // then we offset it by 1, because we want the index and not length.
        var attributeIndex = text.IndexOf(attribute, StringComparison.OrdinalIgnoreCase);
        var maxIndex = text.Length - attribute.Length - 2;
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

    [GeneratedRegex(@"\.pt(?<partNumber>\d+)(?:\.[a-z0-9]+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex GetPartRegex();

    public static bool TryGetAttributeValue(this string text, string attribute, [NotNullWhen(true)] out string? value)
    {
        value = GetAttributeValue(text, attribute);

        // Select the correct id for the part number in the stringified list of file ids.
        if (!string.IsNullOrEmpty(value) && attribute == ShokoFileId.Name && GetPartRegex().Match(text) is { Success: true } regexResult) {
            var partNumber = int.Parse(regexResult.Groups["partNumber"].Value);
            var index = partNumber - 1;
            value = value.Split(',')[index];
        }

        return !string.IsNullOrEmpty(value);
    }

}