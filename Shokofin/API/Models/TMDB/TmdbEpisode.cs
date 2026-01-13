using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Jellyfin.Data.Enums;
using Shokofin.API.Info.TMDB;

namespace Shokofin.API.Models.TMDB;

/// <summary>
/// APIv3 The Movie DataBase (TMDB) Episode Data Transfer Object (DTO).
/// </summary>
public class TmdbEpisode : ITmdbEntity {
    /// <summary>
    /// TMDB Episode ID.
    /// </summary>
    [JsonPropertyName("ID")]
    public int Id { get; set; }

    /// <summary>
    /// TMDB Season ID.
    /// </summary>
    [JsonPropertyName("SeasonID")]
    public string SeasonId { get; set; } = string.Empty;

    /// <summary>
    /// TMDB Show ID.
    /// </summary>
    [JsonPropertyName("ShowID")]
    public int ShowId { get; set; }

    /// <summary>
    /// The ID of the alternate ordering currently in use for the episode.
    /// </summary>
    [JsonPropertyName("AlternateOrderingID")]
    public string AlternateOrderingId { get; init; } = string.Empty;

    /// <summary>
    /// TVDB Episode ID, if available.
    /// </summary>
    [JsonPropertyName("TvdbEpisodeID")]
    public int? TvdbEpisodeId { get; set; }

    /// <summary>
    /// Preferred title based upon episode title preference.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// All available titles for the episode, if they should be included.
    /// </summary>
    public IReadOnlyList<Title> Titles { get; set; } = [];

    /// <summary>
    /// Preferred overview based upon episode title preference.
    /// </summary>
    public string Overview { get; set; } = string.Empty;

    /// <summary>
    /// All available overviews for the episode, if they should be included.
    /// </summary>
    public IReadOnlyList<Text> Overviews { get; set; } = [];

    /// <summary>
    /// The episode number for the main ordering or alternate ordering in use.
    /// </summary>
    public int EpisodeNumber { get; set; }

    /// <summary>
    /// The season number for the main ordering or alternate ordering in use.
    /// </summary>
    public int SeasonNumber { get; set; }

    /// <summary>
    /// User rating of the episode from TMDB users.
    /// </summary>
    public Rating UserRating { get; set; } = new();

    /// <summary>
    /// The episode run-time, if it is known.
    /// </summary>
    public TimeSpan? Runtime { get; set; }

    /// <summary>
    /// The cast that have worked on this show across all episodes and all seasons.
    /// </summary>
    public IReadOnlyList<Role> Cast { get; set; } = [];

    /// <summary>
    /// The crew that have worked on this show across all episodes and all seasons.
    /// </summary>
    public IReadOnlyList<Role> Crew { get; set; } = [];

    /// <summary>
    /// All available ordering for the episode, if they should be included.
    /// </summary>
    public IReadOnlyList<OrderingInformation> Ordering { get; init; } = [];

    /// <summary>
    /// TMDB episode to file cross-references.
    /// </summary>
    public IReadOnlyList<CrossReference> FileCrossReferences { get; set; } = [];

    /// <summary>
    /// The date the episode first aired, if it is known.
    /// </summary>
    public DateOnly? AiredAt { get; set; }

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

    BaseItemKind ITmdbEntity.Kind => BaseItemKind.Episode;

    public TmdbEpisodeInfo ToInfo() => new() {
        TmdbShowId = ShowId.ToString(),
        TmdbAlternateOrderingId = AlternateOrderingId.ToString(),
        TmdbSeasonId = SeasonId.ToString(),
        TmdbEpisodeId = Id.ToString(),
        SeasonNumber = SeasonNumber,
        EpisodeNumber = EpisodeNumber,
        TvdbEpisodeId = TvdbEpisodeId?.ToString(),
        OriginalEpisodeNumber = AlternateOrderingId != ShowId.ToString() ? Ordering.First(o => o.IsDefault).EpisodeNumber : null,
        OriginalSeasonNumber = AlternateOrderingId != ShowId.ToString() ? Ordering.First(o => o.IsDefault).SeasonNumber : null,
    };

    public class OrderingInformation {
#if DEBUG
        /// <summary>
        /// The ordering ID.
        /// </summary>
        public string OrderingID { get; set; } = string.Empty;

        /// <summary>
        /// The alternate ordering type. Will not be set if the main ordering is
        /// used.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AlternateOrderingType? OrderingType { get; set; }

        /// <summary>
        /// English name of the alternate ordering scheme.
        /// </summary>
        public string OrderingName { get; set; } = string.Empty;

        /// <summary>
        /// The season id. Will be a stringified integer for the main ordering,
        /// or a hex id any alternate ordering.
        /// </summary>
        public string SeasonID { get; set; } = string.Empty;

        /// <summary>
        /// English name of the season.
        /// </summary>
        public string SeasonName { get; set; } = string.Empty;
#endif

        /// <summary>
        /// The season number for the ordering.
        /// </summary>
        public int SeasonNumber { get; set; }

        /// <summary>
        /// The episode number for the ordering.
        /// </summary>
        public int EpisodeNumber { get; set; }

        /// <summary>
        /// Indicates the current ordering is the default ordering for the episode.
        /// </summary>
        public bool IsDefault { get; set; }

#if DEBUG
        /// <summary>
        /// Indicates the current ordering is the preferred ordering for the episode.
        /// </summary>
        public bool IsPreferred { get; set; }

        /// <summary>
        /// Indicates the current ordering is in use for the episode.
        /// </summary>
        public bool InUse { get; set; }
#endif
    }
}
