using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Shokofin.API.Models
{
    public class Episode : BaseModel
    {
        public EpisodeIDs IDs { get; set; }

        public DateTime? Watched { get; set; }

        public class AniDB
        {
            public int ID { get; set; }

            [JsonConverter(typeof(JsonStringEnumConverter))]
            public EpisodeType Type { get; set; }

            public int EpisodeNumber { get; set; }

            public DateTime? AirDate { get; set; }

            public List<Title> Titles { get; set; }

            public string Description { get; set; }

            public Rating Rating { get; set; }
        }

        public class TvDB
        {
            public int ID { get; set; }

            public int Season { get; set; }

            public int Number { get; set; }

            public int AbsoluteNumber { get; set; }

            public string Title { get; set; }

            public string Description { get; set; }

            public DateTime? AirDate { get; set; }

            public int AirsAfterSeason { get; set; }

            public int AirsBeforeSeason { get; set; }

            public int AirsBeforeEpisode { get; set; }

            public Rating Rating { get; set; }

            public Image Thumbnail { get; set; }
        }

        public class EpisodeIDs : IDs
        {
            public int AniDB { get; set; }

            public List<int> TvDB { get; set; } = new List<int>();
        }
    }


    public enum EpisodeType
    {
        /// <summary>
        /// The episode type is unknown.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// A catch-all type for future extensions when a provider can't use a current episode type, but knows what the future type should be.
        /// </summary>
        Other = 1,

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
}
