using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Shokofin.API.Models.AniDB;

namespace Shokofin.API.Models.Shoko;

public class ShokoSeries {
    public string Id => IDs.Shoko.ToString();

    /// <summary>
    /// All identifiers related to the series entry, e.g. the Shoko, AniDB,
    /// TMDB, etc.
    /// </summary>
    public SeriesIDs IDs { get; set; } = new();

    /// <summary>
    /// The preferred name of the series based on the selected series language
    /// settings on the server.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The preferred description of the series based on the selected series
    /// language settings on the server.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The yearly seasons this series belongs to.
    /// </summary>
    public List<YearlySeason> YearlySeasons { get; set; } = [];

    /// <summary>
    /// The AniDB entry.
    /// </summary>
    public AnidbAnimeWithDate AniDB { get; set; } = new();

    /// <summary>
    /// Different size metrics for the series.
    /// </summary>
    public SeriesSizes Sizes { get; set; } = new();

    /// <summary>
    /// When the series entry was created during the process of the first file
    /// being added to Shoko.
    /// </summary>
    [JsonPropertyName("Created")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the series entry was last updated.
    /// </summary>
    [JsonPropertyName("Updated")]
    public DateTime LastUpdatedAt { get; set; }

    public class SeriesIDs : IDs {
        /// <summary>
        /// The ID of the direct parent group, if it has one.
        /// </summary>
        public int ParentGroup { get; set; } = 0;

        /// <summary>
        /// The ID of the top-level (ancestor) group this series belongs to.
        /// </summary>
        public int TopLevelGroup { get; set; } = 0;

        /// <summary>
        /// The AniDB ID
        /// </summary>
        public int AniDB { get; set; } = 0;

        /// <summary>
        /// The TvDB IDs
        /// </summary>
        public List<int> TvDB { get; set; } = [];

        /// <summary>
        /// The IMDB Movie IDs.
        /// </summary>
        public List<string> IMDB { get; set; } = [];

        /// <summary>
        /// The Movie Database (TMDB) IDs.
        /// </summary>
        public TmdbSeriesIDs TMDB { get; set; } = new();
    }

    public class TmdbSeriesIDs {
        public List<int> Movie { get; init; } = [];

        public List<int> Show { get; init; } = [];
    }

    /// <summary>
    /// Different size metrics for the series.
    /// </summary>
    public class SeriesSizes {
#if DEBUG
        /// <summary>
        /// Count of hidden episodes, be it available or missing.
        /// </summary>
        public int Hidden { get; set; }
#endif

        /// <summary>
        /// Combined count of all files across all file sources within the series or group.
        /// </summary>
        public int Files =>
            FileSources.Unknown +
            FileSources.Other +
            FileSources.TV +
            FileSources.DVD +
            FileSources.BluRay +
            FileSources.Web +
            FileSources.VHS +
            FileSources.VCD +
            FileSources.LaserDisc +
            FileSources.Camera;

        /// <summary>
        /// Counts of each file source type available within the local collection
        /// </summary>
        public FileSourceCounts FileSources { get; set; } = new();

#if DEBUG
        /// <summary>
        /// What is downloaded and available
        /// </summary>
        public EpisodeTypeCounts Local { get; set; } = new();

        /// <summary>
        /// What is local and watched.
        /// </summary>
        public EpisodeTypeCounts Watched { get; set; } = new();
#endif

        /// <summary>
        /// Total count of each type
        /// </summary>
        public EpisodeTypeCounts Total { get; set; } = new();

        /// <summary>
        /// Lists the count of each type of episode.
        /// </summary>
        public class EpisodeTypeCounts {
            public int Episodes { get; set; }
#if DEBUG
            public int Specials { get; set; }
            public int Credits { get; set; }
            public int Trailers { get; set; }
            public int Parodies { get; set; }
            public int Others { get; set; }
#endif
        }

        public class FileSourceCounts {
            public int Unknown { get; set; }
            public int Other { get; set; }
            public int TV { get; set; }
            public int DVD { get; set; }
            public int BluRay { get; set; }
            public int Web { get; set; }
            public int VHS { get; set; }
            public int VCD { get; set; }
            public int LaserDisc { get; set; }
            public int Camera { get; set; }
        }
    }
}
