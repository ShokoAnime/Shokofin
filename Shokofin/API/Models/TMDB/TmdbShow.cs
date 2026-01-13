using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Jellyfin.Data.Enums;
using Shokofin.API.Info.TMDB;

namespace Shokofin.API.Models.TMDB;

public class TmdbShow : ITmdbParentEntity {
    /// <summary>
    /// TMDB Show ID.
    /// </summary>
    [JsonPropertyName("ID")]
    public int Id { get; set; }

    /// <summary>
    /// TvDB Show ID, if available.
    /// </summary>
    [JsonPropertyName("TvdbID")]
    public int? TvdbId { get; set; }

    /// <summary>
    /// The ID of the alternate ordering currently in use for the show.
    /// </summary>
    [JsonPropertyName("AlternateOrderingID")]
    public string AlternateOrderingId { get; init; } = string.Empty;

    /// <summary>
    /// Preferred title based upon series title preference.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// All available titles, if they should be included.
    /// </summary>
    public IReadOnlyList<Title> Titles { get; set; } = [];

    /// <summary>
    /// Preferred overview based upon description preference.
    /// </summary>
    public string Overview { get; set; } = string.Empty;

    /// <summary>
    /// All available overviews for the series, if they should be included.
    /// </summary>
    public IReadOnlyList<Text> Overviews { get; set; } = [];

    /// <summary>
    /// Original language the show was shot in.
    /// </summary>
    public string OriginalLanguage { get; set; } = string.Empty;

    /// <summary>
    /// Indicates the show is restricted to an age group above the legal age,
    /// because it's a pornography.
    /// </summary>
    public bool IsRestricted { get; set; }

    /// <summary>
    /// User rating of the show from TMDB users.
    /// </summary>
    public Rating UserRating { get; set; } = new();

    /// <summary>
    /// Genres.
    /// </summary>
    public IReadOnlyList<string> Genres { get; set; } = [];

    /// <summary>
    /// Keywords.
    /// </summary>
    public IReadOnlyList<string> Keywords { get; set; } = [];

    /// <summary>
    /// Content ratings for different countries for this show.
    /// </summary>
    public IReadOnlyList<ContentRating> ContentRatings { get; set; } = [];

    /// <summary>
    /// The production countries.
    /// </summary>
    public IReadOnlyDictionary<string, string> ProductionCountries { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// The production companies (studios) that produced the show.
    /// </summary>
    public IReadOnlyList<Studio> Studios { get; set; } = [];

    /// <summary>
    /// Count of episodes associated with the show.
    /// </summary>
    public int EpisodeCount { get; set; }

    /// <summary>
    /// Count of seasons associated with the show.
    /// </summary>
    public int SeasonCount { get; set; }

    /// <summary>
    /// Count of locally alternate ordering schemes associated with the show.
    /// </summary>
    public int AlternateOrderingCount { get; set; }

    /// <summary>
    /// The date the first episode aired at, if it is known.
    /// </summary>
    public DateOnly? FirstAiredAt { get; set; }

    /// <summary>
    /// The date the last episode aired at, if it is known.
    /// </summary>
    public DateOnly? LastAiredAt { get; set; }

    /// <summary>
    /// When the local metadata was first created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the local metadata was last updated with new changes from the
    /// remote.
    /// </summary>
    public DateTime LastUpdatedAt { get; set; }

    string ITmdbEntity.Id => Id.ToString();

    BaseItemKind ITmdbEntity.Kind => BaseItemKind.Series;

    public TmdbShowInfo ToInfo() => new() {
        TmdbShowId = Id.ToString(),
        TmdbAlternateOrderingId = AlternateOrderingId,
        TvdbShowId = TvdbId?.ToString(),
    };
}
