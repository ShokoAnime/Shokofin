
using System.Text.Json.Serialization;

namespace Shokofin.API.Models.TMDB;

/// <summary>
/// APIv3 The Movie DataBase (TMDB) Movie Cross-Reference Data Transfer Object (DTO).
/// </summary>
public class TmdbMovieCrossReference {
    /// <summary>
    /// AniDB Anime ID.
    /// </summary>
    [JsonPropertyName("AnidbAnimeID")]
    public int AnidbAnimeId { get; init; }

    /// <summary>
    /// AniDB Episode ID.
    /// </summary>
    [JsonPropertyName("AnidbEpisodeID")]
    public int AnidbEpisodeId { get; init; }

    /// <summary>
    /// TMDB Show ID.
    /// </summary>
    [JsonPropertyName("TmdbMovieID")]
    public int TmdbMovieId { get; init; }

    /// <summary>
    /// The match rating.
    /// </summary>
    public string Rating { get; init; } = string.Empty;
}