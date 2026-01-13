using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Jellyfin.Data.Enums;

namespace Shokofin.API.Models.TMDB;

public class TmdbMovieCollection : ITmdbEntity {
    /// <summary>
    /// TMDB Movie Collection ID.
    /// </summary>
    [JsonPropertyName("ID")]
    public int Id { get; init; }

    /// <summary>
    /// Preferred title based upon series title preference.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// All available titles for the movie collection, if they should be included.
    /// </summary>
    public IReadOnlyList<Title> Titles { get; init; } = [];

    /// <summary>
    /// Preferred overview based upon description preference.
    /// </summary>
    public string Overview { get; init; } = string.Empty;

    /// <summary>
    /// All available overviews for the movie collection, if they should be included.
    /// </summary>
    public IReadOnlyList<Text> Overviews { get; init; } = [];

    public int MovieCount { get; init; }

    /// <summary>
    /// When the local metadata was first created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// When the local metadata was last updated with new changes from the
    /// remote.
    /// </summary>
    public DateTime LastUpdatedAt { get; init; }

    string ITmdbEntity.Id => Id.ToString();

    BaseItemKind ITmdbEntity.Kind => BaseItemKind.BoxSet;
}

