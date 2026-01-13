using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Jellyfin.Data.Enums;
using Shokofin.API.Info.TMDB;

namespace Shokofin.API.Models.TMDB;

public class TmdbSeason : ITmdbEntity {
    /// <summary>
    /// TMDB Season ID.
    /// </summary>
    [JsonPropertyName("ID")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// TMDB Show ID.
    /// </summary>
    [JsonPropertyName("ShowID")]
    public int ShowId { get; set; }

    /// <summary>
    /// The alternate ordering this season is associated with. Will be null
    /// for main series seasons.
    /// </summary>
    [JsonPropertyName("AlternateOrderingID")]
    public string AlternateOrderingId { get; set; } = string.Empty;

    /// <summary>
    /// Preferred title based upon episode title preference.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// All available titles for the season, if they should be included.
    /// /// </summary>
    public IReadOnlyList<Title> Titles { get; set; } = [];

    /// <summary>
    /// Preferred overview based upon episode title preference.
    /// </summary>
    public string Overview { get; set; } = string.Empty;

    /// <summary>
    /// All available overviews for the season, if they should be included.
    /// </summary>
    public IReadOnlyList<Text> Overviews { get; set; } = [];

    /// <summary>
    /// The season number for the main ordering or alternate ordering in use.
    /// </summary>
    public int SeasonNumber { get; set; }

    /// <summary>
    /// Count of episodes associated with the season.
    /// </summary>
    public int EpisodeCount { get; set; }

    /// <summary>
    /// Indicates the alternate ordering season is locked. Will not be set if
    /// <seealso cref="AlternateOrderingID"/> is not set.
    /// </summary>
    public bool? IsLocked { get; set; }

    /// <summary>
    /// The yearly seasons this series belongs to.
    /// </summary>
    public List<YearlySeason> YearlySeasons { get; set; } = [];

    /// <summary>
    /// When the local metadata was first created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the local metadata was last updated with new changes from the
    /// remote.
    /// </summary>
    public DateTime LastUpdatedAt { get; set; }

    string ITmdbEntity.Id => Id;

    BaseItemKind ITmdbEntity.Kind => BaseItemKind.Season;

    public TmdbSeasonInfo ToInfo() => new() {
        TmdbShowId = ShowId.ToString(),
        TmdbAlternateOrderingId = AlternateOrderingId,
        TmdbSeasonId = Id,
        SeasonNumber = SeasonNumber,
    };
}

