using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Shokofin.SignalR.Interfaces;

public interface IFileEventArgs
{
    /// <summary>
    /// Shoko file id.
    /// </summary>
    int FileId { get; }

    /// <summary>
    /// The ID of the new import folder the event was detected in.
    /// </summary>
    /// <value></value>
    int ImportFolderId { get; }

    /// <summary>
    /// The relative path of the new file from the import folder base location.
    /// </summary>
    string RelativePath { get; }

    /// <summary>
    /// Cross references of episodes linked to this file.
    /// </summary>
    List<FileCrossReference> CrossReferences { get; }

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