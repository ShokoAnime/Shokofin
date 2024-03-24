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
}