using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Jellyfin.Data.Enums;
using Shokofin.API.Info.TMDB;

namespace Shokofin.API.Models.TMDB;

public class TmdbMovie : ITmdbParentEntity {
    /// <summary>
    /// TMDB Movie ID.
    /// </summary>
    [JsonPropertyName("ID")]
    public int Id { get; set; }

    /// <summary>
    /// TMDB Movie Collection ID, if the movie is in a movie collection on TMDB.
    /// </summary>
    [JsonPropertyName("CollectionID")]
    public int? CollectionId { get; set; }

    /// <summary>
    /// IMDB Movie ID, if available.
    /// </summary>
    [JsonPropertyName("ImdbMovieID")]
    public string? ImdbMovieId { get; set; }

    /// <summary>
    /// Preferred title based upon series title preference.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// All available titles for the movie, if they should be included.
    /// </summary>
    public IReadOnlyList<Title> Titles { get; set; } = [];

    /// <summary>
    /// Preferred overview based upon description preference.
    /// </summary>
    public string Overview { get; set; } = string.Empty;

    /// <summary>
    /// All available overviews for the movie, if they should be included.
    /// </summary>
    public IReadOnlyList<Text> Overviews { get; set; } = [];

    /// <summary>
    /// Original language the movie was shot in.
    /// </summary>
    public string OriginalLanguage { get; set; } = string.Empty;

    /// <summary>
    /// Indicates the movie is restricted to an age group above the legal age,
    /// because it's a pornography.
    /// </summary>
    public bool IsRestricted { get; set; }

    /// <summary>
    /// Indicates the entry is not truly a movie, including but not limited to
    /// the types:
    ///
    /// - official compilations,
    /// - best of,
    /// - filmed sport events,
    /// - music concerts,
    /// - plays or stand-up show,
    /// - fitness video,
    /// - health video,
    /// - live movie theater events (art, music),
    /// - and how-to DVDs,
    ///
    /// among others.
    /// </summary>
    public bool IsVideo { get; set; }

    /// <summary>
    /// User rating of the movie from TMDB users.
    /// </summary>
    public Rating UserRating { get; set; } = new();

    /// <summary>
    /// The movie run-time, if it is known.
    /// </summary>
    public TimeSpan? Runtime { get; set; } = null;

    /// <summary>
    /// Genres.
    /// </summary>
    public IReadOnlyList<string> Genres { get; set; } = [];

    /// <summary>
    /// Keywords.
    /// </summary>
    public IReadOnlyList<string> Keywords { get; set; } = [];

    /// <summary>
    /// Content ratings for different countries for this movie.
    /// </summary>
    public IReadOnlyList<ContentRating> ContentRatings { get; set; } = [];

    /// <summary>
    /// The production countries.
    /// </summary>
    public IReadOnlyDictionary<string, string> ProductionCountries { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// The production companies (studios) that produced the movie.
    /// </summary>
    public IReadOnlyList<Studio> Studios { get; set; } = [];

    /// <summary>
    /// The cast that have worked on this movie.
    /// </summary>
    public IReadOnlyList<Role> Cast { get; set; } = [];

    /// <summary>
    /// The crew that have worked on this movie.
    /// </summary>
    public IReadOnlyList<Role> Crew { get; set; } = [];

    /// <summary>
    /// The yearly seasons this series belongs to.
    /// </summary>
    public List<YearlySeason> YearlySeasons { get; set; } = [];

    /// <summary>
    /// TMDB movie to file cross-references.
    /// </summary>
    public IReadOnlyList<CrossReference> FileCrossReferences { get; set; } = [];

    /// <summary>
    /// The date the movie first released, if it is known.
    /// </summary>
    public DateOnly? ReleasedAt { get; set; }

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

    BaseItemKind ITmdbEntity.Kind => BaseItemKind.Movie;

    public TmdbMovieInfo ToInfo() => new() {
        TmdbMovieId = Id.ToString(),
        TmdbMovieCollectionId = CollectionId?.ToString(),
    };
}
