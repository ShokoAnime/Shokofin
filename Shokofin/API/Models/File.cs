using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

#nullable enable
namespace Shokofin.API.Models;

public class File
{
    /// <summary>
    /// The id of the <see cref="File"/>.
    /// </summary>
    [JsonPropertyName("ID")]
    public int Id { get; set; }

    /// <summary>
    /// The Cross Reference Models for every episode this file belongs to, created in a reverse tree and
    /// transformed back into a tree. Series -> Episode such that only episodes that this file is linked to are
    /// shown. In many cases, this will have arrays of 1 item
    /// </summary>
    [JsonPropertyName("SeriesIDs")]
    public List<CrossReference> CrossReferences { get; set; } = new();

    /// <summary>
    /// The calculated hashes from the <see cref="File"/>.
    /// 
    /// Either will all hashes be filled or none.
    /// </summary>
    public HashMap Hashes { get; set; } = new();

    /// <summary>
    /// All the <see cref="Location"/>s this <see cref="File"/> is present at.
    /// </summary>
    public List<Location> Locations { get; set; } = new();

    /// <summary>
    /// Try to fit this file's resolution to something like 1080p, 480p, etc.
    /// </summary>
    public string Resolution { get; set; } = "";

    /// <summary>
    /// The duration of the file.
    /// </summary>
    public TimeSpan Duration { get; set; }

    [JsonPropertyName("Created")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("Updated")]
    public DateTime LastUpdatedAt { get; set; }

    /// <summary>
    /// The size of the file in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Metadata about the location where a file lies, including the import
    /// folder it belogns to and the relative path from the base of the import
    /// folder to where it lies.
    /// </summary>
    public class Location
    {
        /// <summary>
        /// The id of the <see cref="ImportFolder"/> this <see cref="File"/>
        /// resides in.
        /// </summary>
        /// <value></value>
        [JsonPropertyName("ImportFolderID")]
        public int ImportFolderId { get; set; }

        /// <summary>
        /// The relative path from the base of the <see cref="ImportFolder"/> to
        /// where the <see cref="File"/> lies.
        /// </summary>
        [JsonPropertyName("RelativePath")]
        public string RelativePath { get; set; } = "";

        /// <summary>
        /// The relative path from the base of the <see cref="ImportFolder"/> to
        /// where the <see cref="File"/> lies, with a leading slash applied at
        /// the start.
        /// </summary>
        public string Path => "/" + RelativePath;

        /// <summary>
        /// True if the server can access the the <see cref="Location.Path"/> at
        /// the moment of requesting the data.
        /// </summary>
        [JsonPropertyName("Accessible")]
        public bool IsAccessible { get; set; } = false;
    }

    /// <summary>
    /// The calculated hashes of the file. Either will all hashes be filled or
    /// none.
    /// </summary>
    public class HashMap
    {
        public string ED2K { get; set; } = "";

        public string SHA1 { get; set; } = "";

        public string CRC32 { get; set; } = "";

        public string MD5 { get; set; } = "";
    }

    public class CrossReference
    {
        /// <summary>
        /// The Series IDs
        /// </summary>
        [JsonPropertyName("SeriesID")]
        public CrossReferenceIDs Series { get; set; } = new();

        /// <summary>
        /// The Episode IDs
        /// </summary>
        [JsonPropertyName("EpisodeIDs")]
        public List<CrossReferenceIDs> Episodes { get; set; } = new();
    }

    public class CrossReferenceIDs : IDs
    {
        /// <summary>
        /// Any AniDB ID linked to this object
        /// </summary>
        public int AniDB { get; set; }

        /// <summary>
        /// Any TvDB IDs linked to this object
        /// </summary>
        public List<int> TvDB { get; set; } = new();
    }

    /// <summary>
    /// User stats for the file.
    /// </summary>
    public class UserStats
    {
        /// <summary>
        /// Where to resume the next playback.
        /// </summary>
        public TimeSpan? ResumePosition { get; set; }

        /// <summary>
        /// Total number of times the file have been watched.
        /// </summary>
        public int WatchedCount { get; set; }

        /// <summary>
        /// When the file was last watched. Will be null if the full is
        /// currently marked as unwatched.
        /// </summary>
        public DateTime? LastWatchedAt { get; set; }

        /// <summary>
        /// When the entry was last updated.
        /// </summary>
        public DateTime LastUpdatedAt { get; set; }

        /// <summary>
        /// True if the <see cref="UserStats"/> object is considered empty.
        /// </summary>
        public virtual bool IsEmpty
        {
            get => ResumePosition == null && WatchedCount == 0 && WatchedCount == 0;
        }
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FileSource
{
    Unknown = 0,
    Other = 1,
    TV = 2,
    DVD = 3,
    BluRay = 4,
    Web = 5,
    VHS = 6,
    VCD = 7,
    LaserDisc = 8,
    Camera = 9
}
