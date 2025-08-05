using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Shokofin.API.Info.Shoko;
using Shokofin.API.Models.AniDB;

namespace Shokofin.API.Models.Shoko;

public class ShokoEpisode {
    public string Id => IDs.Shoko.ToString();

    /// <summary>
    /// All identifiers related to the episode entry, e.g. the Shoko, AniDB,
    /// TMDB, etc.
    /// </summary>
    public EpisodeIDs IDs { get; set; } = new();

    /// <summary>
    /// The preferred name of the episode based on the selected episode language
    /// settings on the server.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The preferred description of the episode based on the selected episode
    /// language settings on the server.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The duration of the episode.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Indicates the episode is hidden.
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>
    /// Number of files linked to the episode.
    /// </summary>
    /// <value></value>
    public int Size { get; set; }

    /// <summary>
    /// The <see cref="AnidbEpisode"/>, if <see cref="DataSource.AniDB"/> is
    /// included in the data to add.
    /// </summary>
    public AnidbEpisode AniDB { get; set; } = new();

    /// <summary>
    /// File cross-references for the episode.
    /// </summary>
    public List<CrossReference.EpisodeCrossReferenceIDs> CrossReferences { get; set; } = [];

    /// <summary>
    /// When the episode entry was created.
    /// </summary>
    [JsonPropertyName("Created")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the episode entry was last updated.
    /// </summary>
    [JsonPropertyName("Updated")]
    public DateTime LastUpdatedAt { get; set; }

    public ShokoEpisodeInfo ToInfo() => new() {
        ShokoSeriesId = IDs.ParentSeries.ToString(),
        ShokoEpisodeId = Id,
    };

    public class EpisodeIDs : IDs {
        public int ParentSeries { get; set; }

        public int AniDB { get; set; }

        public List<int> TvDB { get; set; } = [];

        public List<string> IMDB { get; set; } = [];

        public TmdbEpisodeIDs TMDB { get; init; } = new();
    }

    public class TmdbEpisodeIDs {
        public List<int> Episode { get; init; } = [];

        public List<int> Movie { get; init; } = [];

        public List<int> Show { get; init; } = [];
    }
}
