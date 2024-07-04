using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Shokofin;

public static class ListExtensions
{
    public static bool TryRemoveAt<T>(this List<T> list, int index, [NotNullWhen(true)] out T? item)
    {
        if (index < 0 || index >= list.Count) {
            item = default;
            return false;
        }
        item = list[index]!;
        list.RemoveAt(index);
        return true;
    }
}