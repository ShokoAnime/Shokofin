using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Shokofin.API.Info.AniDB;

namespace Shokofin.API.Models.AniDB;

public class AnidbEpisode {
    [JsonPropertyName("ID")]
    public int Id { get; set; }

    [JsonPropertyName("AnimeID")]
    public int AnimeId { get; set; }

    /// <summary>
    /// The duration of the episode.
    /// </summary>
    public TimeSpan Duration { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EpisodeType Type { get; set; }

    public int EpisodeNumber { get; set; }

    public DateTime? AirDate { get; set; }

    public IReadOnlyList<Title> Titles { get; set; } = [];

    public string Description { get; set; } = string.Empty;

    public Rating Rating { get; set; } = new();

    public AnidbEpisodeInfo ToInfo() => new() {
        AnidbAnimeId = AnimeId.ToString(),
        AnidbEpisodeId = Id.ToString(),
        EpisodeNumber = EpisodeNumber,
        Type = Type,
    };
}
