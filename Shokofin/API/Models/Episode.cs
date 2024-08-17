using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

public class Episode
{
    /// <summary>
    /// All identifiers related to the episode entry, e.g. the Shoko, AniDB,
    /// TvDB, etc.
    /// </summary>
    public EpisodeIDs IDs { get; set; } = new();

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The duration of the episode.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Indicates the episode is hidden.
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>
    /// Number of files 
    /// </summary>
    /// <value></value>
    public int Size { get; set; }

    /// <summary>
    /// The <see cref="Episode.AniDB"/>, if <see cref="DataSource.AniDB"/> is
    /// included in the data to add.
    /// </summary>
    [JsonPropertyName("AniDB")]
    public AniDB AniDBEntity { get; set; } = new();

    /// <summary>
    /// The <see cref="Episode.TvDB"/> entries, if <see cref="DataSource.TvDB"/>
    /// is included in the data to add.
    /// </summary>
    [JsonPropertyName("TvDB")]
    public List<TvDB> TvDBEntityList { get; set; } = new();

    /// <summary>
    /// File cross-references for the episode.
    /// </summary>
    public List<CrossReference.EpisodeCrossReferenceIDs> CrossReferences { get; set; } = new();

    public class AniDB
    {
        [JsonPropertyName("ID")]
        public int Id { get; set; }

        /// <summary>
        /// The duration of the episode.
        /// </summary>
        public TimeSpan Duration { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public EpisodeType Type { get; set; }

        public int EpisodeNumber { get; set; }

        public DateTime? AirDate { get; set; }

        public List<Title> Titles { get; set; } = new();

        public string Description { get; set; } = string.Empty;

        public Rating Rating { get; set; } = new();
    }

    public class TvDB
    {
        [JsonPropertyName("Season")]
        public int SeasonNumber { get; set; }

        [JsonPropertyName("Number")]
        public int EpisodeNumber { get; set; }

        public string Description { get; set; } = string.Empty;

        public int? AirsAfterSeason { get; set; }

        public int? AirsBeforeSeason { get; set; }

        public int? AirsBeforeEpisode { get; set; }

        public Image Thumbnail { get; set; } = new();
    }

    public class EpisodeIDs : IDs
    {
        public int ParentSeries { get; set; }

        public int AniDB { get; set; }

        public List<int> TvDB { get; set; } = new();
    }
}


public enum EpisodeType
{
    /// <summary>
    /// A catch-all type for future extensions when a provider can't use a current episode type, but knows what the future type should be.
    /// </summary>
    Other = 1,

    /// <summary>
    /// The episode type is unknown.
    /// </summary>
    Unknown = Other,

    /// <summary>
    /// A normal episode.
    /// </summary>
    Normal = 2,

    /// <summary>
    /// A special episode.
    /// </summary>
    Special = 3,

    /// <summary>
    /// A trailer.
    /// </summary>
    Trailer = 4,

    /// <summary>
    /// Either an opening-song, or an ending-song.
    /// </summary>
    ThemeSong = 5,

    /// <summary>
    /// Intro, and/or opening-song.
    /// </summary>
    OpeningSong = 6,

    /// <summary>
    /// Outro, end-roll, credits, and/or ending-song.
    /// </summary>
    EndingSong = 7,

    /// <summary>
    /// AniDB parody type. Where else would this be useful?
    /// </summary>
    Parody = 8,

    /// <summary>
    /// A interview tied to the series.
    /// </summary>
    Interview = 9,

    /// <summary>
    /// A DVD or BD extra, e.g. BD-menu or deleted scenes.
    /// </summary>
    Extra = 10,
}

