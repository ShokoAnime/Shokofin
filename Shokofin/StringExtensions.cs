using System.Collections.Generic;

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
}