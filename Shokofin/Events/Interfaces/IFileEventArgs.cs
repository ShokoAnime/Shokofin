using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Shokofin.Events.Interfaces;

public interface IFileEventArgs
{
    /// <summary>
    /// Shoko file id.
    /// </summary>
    int FileId { get; }

    /// <summary>
    /// Shoko file location id, if available.
    /// </summary>
    int? FileLocationId { get; }

    /// <summary>
    /// The ID of the new import folder the event was detected in.
    /// </summary>
    /// <value></value>
    int ImportFolderId { get; }

    /// <summary>
    /// The relative path from the base of the <see cref="ImportFolder"/> to
    /// where the <see cref="File"/> lies, with a leading slash applied at
    /// the start and normalized for the local system.
    /// </summary>
    string RelativePath { get; }

    /// <summary>
    /// Indicates that the event has cross references provided. They may still
    /// be empty, but now we don't need to fetch them separately.
    /// </summary>
    bool HasCrossReferences { get; }

    /// <summary>
    /// Cross references of episodes linked to this file.
    /// </summary>
    List<FileCrossReference> CrossReferences { get; }

    public class FileCrossReference
    {
        /// <summary>
        /// AniDB episode id.
        /// </summary>
        [JsonPropertyName("AnidbEpisodeID")]
        public int AnidbEpisodeId { get; set; }

        /// <summary>
        /// AniDB anime id.
        /// </summary>
        [JsonPropertyName("AnidbAnimeID")]
        public int AnidbAnimeId { get; set; }

        /// <summary>
        /// Shoko episode id.
        /// </summary>
        [JsonPropertyName("EpisodeID")]
        public int? ShokoEpisodeId { get; set; }

        /// <summary>
        /// Shoko series id.
        /// </summary>
        [JsonPropertyName("SeriesID")]
        public int? ShokoSeriesId { get; set; }
    }
}