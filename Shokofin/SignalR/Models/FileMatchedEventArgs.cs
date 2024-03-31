using System.Collections.Generic;
using System.Text.Json.Serialization;

#nullable enable
namespace Shokofin.SignalR.Models;

public class FileMatchedEventArgs : FileEventArgs
{
    /// <summary>
    /// Cross references of episodes linked to this file.
    /// </summary>
    [JsonPropertyName("CrossRefs")]
    public List<FileCrossReference> CrossReferences { get; set; } = new();

    public class FileCrossReference
    {
        /// <summary>
        /// Shoko episode id.
        /// </summary>
        [JsonPropertyName("EpisodeID")]
        public int EpisodeId { get; set; }

        /// <summary>
        /// Shoko series id.
        /// </summary>
        [JsonPropertyName("SeriesID")]
        public int SeriesId { get; set; }

        /// <summary>
        /// Shoko group id.
        /// </summary>
        [JsonPropertyName("GroupID")]
        public int GroupId { get; set; }
    }
}