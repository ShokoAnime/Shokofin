
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Shokofin.API.Models.AniDB;

public class AnidbAnime {
    /// <summary>
    /// AniDB Id
    /// </summary>
    [JsonPropertyName("ID")]
    public int Id { get; set; }

    /// <summary>
    /// <see cref="Shoko.ShokoSeries"/> Id if the series is available locally.
    /// </summary>
    [JsonPropertyName("ShokoID")]
    public int? ShokoId { get; set; }

    /// <summary>
    /// Series type. Series, OVA, Movie, etc
    /// </summary>
    public SeriesType Type { get; set; }

    /// <summary>
    /// Main Title, usually matches x-jat
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// There should always be at least one of these, the <see cref="Title"/>. May be omitted if needed.
    /// </summary>
    public IReadOnlyList<Title>? Titles { get; set; }

    /// <summary>
    /// Description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Restricted content. Mainly porn.
    /// </summary>
    public bool Restricted { get; set; }

    /// <summary>
    /// The main or default poster.
    /// </summary>
    public Image Poster { get; set; } = new();

    /// <summary>
    /// Number of <see cref="EpisodeType.Normal"/> episodes contained within the series if it's known.
    /// </summary>
    public int? EpisodeCount { get; set; }

    /// <summary>
    /// The average rating for the anime. Only available on
    /// </summary>
    public Rating? Rating { get; set; }

    /// <summary>
    /// User approval rate for the similar submission. Only available for similar.
    /// </summary>
    public Rating? UserApproval { get; set; }

    /// <summary>
    /// Relation type. Only available for relations.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RelationType? Relation { get; set; }
}

public class AnidbAnimeWithDate : AnidbAnime {
    /// <summary>
    /// Description.
    /// </summary>
    public new string Description { get; set; } = string.Empty;

    /// <summary>
    /// There should always be at least one of these, the <see cref="Title"/>. May be omitted if needed.
    /// </summary>
    public new List<Title> Titles { get; set; } = [];

    /// <summary>
    /// The average rating for the anime. Only available on
    /// </summary>
    public new Rating Rating { get; set; } = new();

    /// <summary>
    /// Number of <see cref="EpisodeType.Normal"/> episodes contained within the series if it's known.
    /// </summary>
    public new int EpisodeCount { get; set; }

    [JsonIgnore]
    private DateTime? InternalAirDate { get; set; } = null;

    /// <summary>
    /// Air date (2013-02-27). Anything without an air date is going to be missing a lot of info.
    /// </summary>
    public DateTime? AirDate {
        get {
            return InternalAirDate;
        }
        set {
            InternalAirDate = value.HasValue && (value.Value == DateTime.UnixEpoch || value.Value == DateTime.MinValue || value.Value == DateTime.MaxValue) ? null : value;
        }
    }

    [JsonIgnore]
    private DateTime? InternalEndDate { get; set; } = null;

    /// <summary>
    /// End date, can be omitted. Omitted means that it's still airing (2013-02-27)
    /// </summary>
    public DateTime? EndDate {
        get {
            return InternalEndDate;
        }
        set {
            InternalEndDate = value.HasValue && (value.Value == DateTime.UnixEpoch || value.Value == DateTime.MinValue || value.Value == DateTime.MaxValue) ? null : value;
        }
    }
}