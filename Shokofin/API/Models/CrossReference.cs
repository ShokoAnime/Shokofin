using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

public class CrossReference
{
    /// <summary>
    /// The Series IDs
    /// </summary>
    [JsonPropertyName("SeriesID")]
    public SeriesCrossReferenceIDs Series { get; set; } = new();

    /// <summary>
    /// The Episode IDs
    /// </summary>
    [JsonPropertyName("EpisodeIDs")]
    public List<EpisodeCrossReferenceIDs> Episodes { get; set; } = new();

    /// <summary>
    /// File episode cross-reference for a series.
    /// </summary>
    public class EpisodeCrossReferenceIDs
    {
        /// <summary>
        /// The Shoko ID, if the local metadata has been created yet.
        /// </summary>
        [JsonPropertyName("ID")]
        public int? Shoko { get; set; }

        /// <summary>
        /// The AniDB ID.
        /// </summary>
        public int AniDB { get; set; }

        public int? ReleaseGroup { get; set; }

        /// <summary>
        /// Percentage file is matched to the episode.
        /// </summary>
        public CrossReferencePercentage? Percentage { get; set; }
    }

    public class CrossReferencePercentage
    {
        /// <summary>
        /// File/episode cross-reference percentage range end.
        /// </summary>
        public int Start { get; set; }

        /// <summary>
        /// File/episode cross-reference percentage range end.
        /// </summary>
        public int End { get; set; }

        /// <summary>
        /// The raw percentage to "group" the cross-references by.
        /// </summary>
        public int Size { get; set; }

        /// <summary>
        /// The assumed number of groups in the release, to group the
        /// cross-references by.
        /// </summary>
        public int? Group { get; set; }
    }

    /// <summary>
    /// File series cross-reference.
    /// </summary>
    public class SeriesCrossReferenceIDs
    {
        /// <summary>
        /// The Shoko ID, if the local metadata has been created yet.
        /// /// </summary>
        [JsonPropertyName("ID")]
        
        public int? Shoko { get; set; }

        /// <summary>
        /// The AniDB ID.
        /// </summary>
        public int AniDB { get; set; }
    }
}