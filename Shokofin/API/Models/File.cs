using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

public class File {
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
    public List<CrossReference> CrossReferences { get; set; } = [];

    /// <summary>
    /// Indicates this file is marked as a variation in Shoko Server.
    /// </summary>
    public bool IsVariation { get; set; }

    /// <summary>
    /// All the <see cref="Location"/>s this <see cref="File"/> is present at.
    /// </summary>
    public List<Location> Locations { get; set; } = [];

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

    [JsonPropertyName("Release")]
    public ReleaseInfo? Release { get; set; }

    [JsonPropertyName("AniDB")]
    public ReleaseInfo? LegacyRelease {
        get => Release;
        set => Release = value;
    }

    /// <summary>
    /// The size of the file in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Metadata about the location where a file lies, including the import
    /// folder it belongs to and the relative path from the base of the import
    /// folder to where it lies.
    /// </summary>
    public class Location {
        /// <summary>
        /// File location ID.
        /// </summary>
        [JsonPropertyName("ID")]
        public int? Id { get; set; }

        /// <summary>
        /// The id of the <see cref="ManagedFolder"/> this <see cref="File"/>
        /// resides in.
        /// </summary>
        [JsonPropertyName("ImportFolderID")]
        public int ImportFolderId {
            get => ManagedFolderId;
            set => ManagedFolderId = value;
        }

        /// <summary>
        /// The id of the <see cref="ManagedFolder"/> this <see cref="File"/>
        /// resides in.
        /// </summary>
        [JsonPropertyName("ManagedFolderID")]
        public int ManagedFolderId { get; set; }

        /// <summary>
        /// The relative path from the base of the <see cref="ManagedFolder"/> to
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
        /// The relative path from the base of the <see cref="ManagedFolder"/> to
        /// where the <see cref="File"/> lies, with a leading slash applied at
        /// the start.
        /// </summary>
        [JsonIgnore]
        public string RelativePath {
            get {
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
    /// User stats for the file.
    /// </summary>
    public class UserStats {
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
        public virtual bool IsEmpty {
            get => ResumePosition == null && LastWatchedAt == null && WatchedCount == 0;
        }
    }
}
