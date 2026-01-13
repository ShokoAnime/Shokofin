using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Shokofin.Extensions;

public static class ListExtensions {
    public static bool TryRemoveAt<T>(this List<T> list, int index, [NotNullWhen(true)] out T? item) {
        if (index < 0 || index >= list.Count) {
            item = default;
            return false;
        }
        item = list[index]!;
        list.RemoveAt(index);
        return true;
    }

    public static IEnumerable<T> GetRange<T>(this IReadOnlyList<T> list, int start, int end) {
        if (start < 0 || start >= list.Count)
            yield break;

        for (var index = 0; index < end - start; index++) {
            yield return list[start + index];
        }
    }
}