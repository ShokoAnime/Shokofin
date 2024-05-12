using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

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
    public string Resolution { get; set; } = string.Empty;

    /// <summary>
    /// The duration of the file.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// The file creation date of this file.
    /// </summary>
    [JsonPropertyName("Created")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the file was last imported. Usually is a file only imported once,
    /// but there may be exceptions.
    /// </summary>
    [JsonPropertyName("Imported")]
    public DateTime? ImportedAt { get; set; }

    [JsonPropertyName("AniDB")]
    public AniDB? AniDBData { get; set; }

    /// <summary>
    /// The size of the file in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Metadata about the location where a file lies, including the import
    /// folder it belongs to and the relative path from the base of the import
    /// folder to where it lies.
    /// </summary>
    public class Location
    {
        /// <summary>
        /// The id of the <see cref="ImportFolder"/> this <see cref="File"/>
        /// resides in.
        /// </summary>
        [JsonPropertyName("ImportFolderID")]
        public int ImportFolderId { get; set; }

        /// <summary>
        /// The relative path from the base of the <see cref="ImportFolder"/> to
        /// where the <see cref="File"/> lies.
        /// </summary>
        [JsonPropertyName("RelativePath")]
        public string InternalPath { get; set; } = string.Empty;

        /// <summary>
        /// Cached path for later re-use.
        /// </summary>
        [JsonIgnore]
        private string? CachedPath { get; set; }

        /// <summary>
        /// The relative path from the base of the <see cref="ImportFolder"/> to
        /// where the <see cref="File"/> lies, with a leading slash applied at
        /// the start.
        /// </summary>
        [JsonIgnore]
        public string RelativePath
        {
            get
            {
                if (CachedPath != null)
                    return CachedPath;
                var relativePath = InternalPath
                    .Replace('/', System.IO.Path.DirectorySeparatorChar)
                    .Replace('\\', System.IO.Path.DirectorySeparatorChar);
                if (relativePath[0] != System.IO.Path.DirectorySeparatorChar)
                    relativePath = System.IO.Path.DirectorySeparatorChar + relativePath;
                return CachedPath = relativePath;
            }
        }

        /// <summary>
        /// True if the server can access the the <see cref="Location.RelativePath"/> at
        /// the moment of requesting the data.
        /// </summary>
        [JsonPropertyName("Accessible")]
        public bool IsAccessible { get; set; } = false;
    }

    /// <summary>
    /// AniDB_File info
    /// </summary>
    public class AniDB
    {
        /// <summary>
        /// The AniDB File ID.
        /// </summary>
        [JsonPropertyName("ID")]
        public int Id { get; set; }

        /// <summary>
        /// Blu-ray, DVD, LD, TV, etc..
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public FileSource Source { get; set; }

        /// <summary>
        /// The Release Group. This is usually set, but sometimes is set as "raw/unknown"
        /// </summary>
        public AniDBReleaseGroup ReleaseGroup { get; set; } = new();

        /// <summary>
        /// The file's version, Usually 1, sometimes more when there are edits released later
        /// </summary>
        public int Version { get; set; }

        /// <summary>
        /// The original FileName. Useful for when you obtained from a shady source or when you renamed it without thinking. 
        /// </summary>
        public string OriginalFileName { get; set; } = string.Empty;

        /// <summary>
        /// Is the file marked as deprecated. Generally, yes if there's a V2, and this isn't it
        /// </summary>
        public bool IsDeprecated { get; set; }

        /// <summary>
        /// Mostly applicable to hentai, but on occasion a TV release is censored enough to earn this.
        /// </summary>
        public bool? IsCensored { get; set; }

        /// <summary>
        /// Does the file have chapters. This may be wrong, since it was only added in AVDump2 (a more recent version at that)
        /// </summary>
        [JsonPropertyName("Chaptered")]
        public bool IsChaptered { get; set; }

        /// <summary>
        /// The file's release date. This is probably not filled in
        /// </summary>
        [JsonPropertyName("ReleaseDate")]
        public DateTime? ReleasedAt { get; set; }

        /// <summary>
        /// When we last got data on this file
        /// </summary>
        [JsonPropertyName("Updated")]
        public DateTime LastUpdatedAt { get; set; }
    }

    public class AniDBReleaseGroup
    {
        /// <summary>
        /// The AniDB Release Group ID.
        /// /// </summary>
        [JsonPropertyName("ID")]
        public int Id { get; set; }

        /// <summary>
        /// The release group's Name (Unlimited Translation Works)
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// The release group's Name (UTW)
        /// </summary>
        public string? ShortName { get; set; }
    }

    /// <summary>
    /// The calculated hashes of the file. Either will all hashes be filled or
    /// none.
    /// </summary>
    public class HashMap
    {
        public string ED2K { get; set; } = string.Empty;

        public string SHA1 { get; set; } = string.Empty;

        public string CRC32 { get; set; } = string.Empty;

        public string MD5 { get; set; } = string.Empty;
    }

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
            get => ResumePosition == null && LastWatchedAt == null && WatchedCount == 0;
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
