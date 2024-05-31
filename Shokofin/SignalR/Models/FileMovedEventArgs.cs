using System.Collections.Generic;
using System.Text.Json.Serialization;
using Shokofin.Events.Interfaces;

namespace Shokofin.SignalR.Models;


public class FileMovedEventArgs: FileEventArgs, IFileRelocationEventArgs
{
    /// <inheritdoc/>
    [JsonInclude, JsonPropertyName("PreviousImportFolderID")]
    public int PreviousImportFolderId { get; set; }

    /// <summary>
    /// The previous relative path with no leading slash and directory
    /// separators used on the Shoko side.
    /// </summary>
    [JsonInclude, JsonPropertyName("PreviousRelativePath")]
    public string PreviousInternalPath  { get; set; } = string.Empty;

    /// <summary>
    /// Cached path for later re-use.
    /// </summary>
    [JsonIgnore]
    private string? PreviousCachedPath { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public string PreviousRelativePath
    {
        get
        {
            if (PreviousCachedPath != null)
                return PreviousCachedPath;
            var relativePath = PreviousInternalPath
                .Replace('/', System.IO.Path.DirectorySeparatorChar)
                .Replace('\\', System.IO.Path.DirectorySeparatorChar);
            if (relativePath[0] != System.IO.Path.DirectorySeparatorChar)
                relativePath = System.IO.Path.DirectorySeparatorChar + relativePath;
            return PreviousCachedPath = relativePath;
        }
    }

    public class V0 : IFileRelocationEventArgs
    {
        /// <inheritdoc/>
        [JsonInclude, JsonPropertyName("FileID")]
        public int FileId { get; set; }

        /// <inheritdoc/>
        [JsonIgnore]
        public int? FileLocationId => null;

        /// <inheritdoc/>
        [JsonInclude, JsonPropertyName("NewImportFolderID")]
        public int ImportFolderId { get; set; }

        /// <inheritdoc/>
        [JsonInclude, JsonPropertyName("OldImportFolderID")]
        public int PreviousImportFolderId { get; set; }

        /// <summary>
        /// The relative path with no leading slash and directory separators used on
        /// the Shoko side.
        /// </summary>
        [JsonInclude, JsonPropertyName("NewRelativePath")]
        public string InternalPath  { get; set; } = string.Empty;

        /// <summary>
        /// Cached path for later re-use.
        /// </summary>
        [JsonIgnore]
        private string? CachedPath { get; set; }

        /// <inheritdoc/>
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
        /// The previous relative path with no leading slash and directory
        /// separators used on the Shoko side.
        /// </summary>
        [JsonInclude, JsonPropertyName("OldRelativePath")]
        public string PreviousInternalPath  { get; set; } = string.Empty;

        /// <summary>
        /// Cached path for later re-use.
        /// </summary>
        [JsonIgnore]
        private string? PreviousCachedPath { get; set; }

        /// <inheritdoc/>
        [JsonIgnore]
        public string PreviousRelativePath
        {
            get
            {
                if (PreviousCachedPath != null)
                    return PreviousCachedPath;
                var relativePath = PreviousInternalPath
                    .Replace('/', System.IO.Path.DirectorySeparatorChar)
                    .Replace('\\', System.IO.Path.DirectorySeparatorChar);
                if (relativePath[0] != System.IO.Path.DirectorySeparatorChar)
                    relativePath = System.IO.Path.DirectorySeparatorChar + relativePath;
                return PreviousCachedPath = relativePath;
            }
        }

        /// <inheritdoc/>
        [JsonIgnore]
        public bool HasCrossReferences => false;

        /// <inheritdoc/>
        [JsonIgnore]
        public List<IFileEventArgs.FileCrossReference> CrossReferences => new();
    }
}
