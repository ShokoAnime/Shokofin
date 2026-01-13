using System.Text.Json.Serialization;

namespace Shokofin.API.Models.TMDB;

/// <summary>
/// APIv3 The Movie DataBase (TMDB) Episode Cross-Reference Data Transfer Object (DTO).
/// </summary>
public class TmdbEpisodeCrossReference {
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
    [JsonPropertyName("TmdbShowID")]
    public int TmdbShowId { get; init; }

    /// <summary>
    /// TMDB Episode ID. Will be <c>0</c> if the <see cref="AnidbEpisodeID"/>
    /// is not mapped to a TMDB Episode yet.
    /// </summary>
    [JsonPropertyName("TmdbEpisodeID")]
    public int TmdbEpisodeId { get; init; }

    /// <summary>
    /// The index to order the cross-references if multiple references
    /// exists for the same anidb or tmdb episode.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// The match rating.
    /// </summary>
    public string Rating { get; init; } = string.Empty;
}
