
using System.Collections.Generic;

namespace Shokofin.API.Models;

/// <summary>
/// A list with the total count of <typeparamref name="T"/> entries that
/// match the filter and a sliced or the full list of <typeparamref name="T"/>
/// entries.
/// </summary>
public class ListResult<T>
{
    /// <summary>
    /// Total number of <typeparamref name="T"/> entries that matched the
    /// applied filter.
    /// </summary>
    public int Total { get; set; } = 0;

    /// <summary>
    /// A sliced page or the whole list of <typeparamref name="T"/> entries.
    /// </summary>
    public IReadOnlyList<T> List { get; set; } = new T[] {};
}
