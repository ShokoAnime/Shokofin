using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

/// <summary>
/// APIv3 Studio Data Transfer Object (DTO).
/// </summary>
public class Studio {
    /// <summary>
    /// Studio ID relative to the <see cref="Source"/>.
    /// </summary>
    [JsonPropertyName("ID")]
    public int Id { get; init; }

    /// <summary>
    /// The name of the studio.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The country the studio originates from.
    /// </summary>
    public string CountryOfOrigin { get; init; } = string.Empty;

    /// <summary>
    /// Entities produced by the studio in the local collection, both movies
    /// and/or shows.
    /// </summary>
    public int Size { get; init; }

    /// <summary>
    /// Logos used by the studio.
    /// </summary>
    public IReadOnlyList<Image> Logos { get; init; } = [];

    /// <summary>
    /// The source of which the studio metadata belongs to.
    /// </summary>
    public string Source { get; init; } = string.Empty;
}
