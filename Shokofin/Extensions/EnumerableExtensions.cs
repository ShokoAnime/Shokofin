using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Shokofin.Extensions;

public static class EnumerableExtensions {
    [return: NotNullIfNotNull(nameof(enumerable))]
    public static IEnumerable<T>? WhereNotNull<T>(this IEnumerable<T?>? enumerable)
        => enumerable?.Where(a => a is not null).Select(a => a!);

    [return: NotNullIfNotNull(nameof(enumerable))]
    public static IEnumerable<T>? WhereNotNull<T>(this IEnumerable<T?>? enumerable) where T : struct
        => enumerable?.Where(a => a is not null).Select(a => a!.Value);

    [return: NotNullIfNotNull(nameof(enumerable))]
    public static IEnumerable<T>? WhereNotNullOrDefault<T>(this IEnumerable<T?>? enumerable)
        => enumerable?.Where(a => a is not null && !Equals(a, default(T))).Select(a => a!);

    [return: NotNullIfNotNull(nameof(enumerable))]
    public static IEnumerable<T>? WhereNotNullOrDefault<T>(this IEnumerable<T?>? enumerable) where T : struct
        => enumerable?.Where(a => a is not null && !Equals(a, default(T))).Select(a => a!.Value);
}