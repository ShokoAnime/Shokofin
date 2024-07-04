using System.Collections.Generic;
using System.Text.Json.Serialization;
using Shokofin.Events.Interfaces;

namespace Shokofin.SignalR.Models;


public class FileRenamedEventArgs : FileEventArgs, IFileRelocationEventArgs
{
    /// <summary>
    /// The current file name.
    /// </summary>
    [JsonInclude, JsonPropertyName("FileName")]
    public string FileName  { get; set; } = string.Empty;

    /// <summary>
    /// The previous file name.
    /// </summary>
    [JsonInclude, JsonPropertyName("PreviousFileName")]
    public string PreviousFileName  { get; set; } = string.Empty;

    /// <inheritdoc/>
    [JsonIgnore]
    public int PreviousImportFolderId => ImportFolderId;

    /// <inheritdoc/>
    [JsonIgnore]
    public string PreviousRelativePath => RelativePath[..^FileName.Length] + PreviousFileName;

    public class V0 : IFileRelocationEventArgs
    {
        /// <inheritdoc/>
        [JsonInclude, JsonPropertyName("FileID")]
        public int FileId { get; set; }

        /// <inheritdoc/>
        [JsonIgnore]
        public int? FileLocationId => null;

        /// <inheritdoc/>
        [JsonInclude, JsonPropertyName("ImportFolderID")]
        public int ImportFolderId { get; set; }

        /// <summary>
        /// The relative path with no leading slash and directory separators used on
        /// the Shoko side.
        /// </summary>
        [JsonInclude, JsonPropertyName("RelativePath")]
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
        /// The current file name.
        /// </summary>
        [JsonInclude, JsonPropertyName("NewFileName")]
        public string FileName  { get; set; } = string.Empty;

        /// <summary>
        /// The previous file name.
        /// </summary>
        [JsonInclude, JsonPropertyName("OldFileName")]
        public string PreviousFileName  { get; set; } = string.Empty;

        /// <inheritdoc/>
        [JsonIgnore]
        public int PreviousImportFolderId => ImportFolderId;

        /// <inheritdoc/>
        [JsonIgnore]
        public string PreviousRelativePath => RelativePath[..^FileName.Length] + PreviousFileName;

        /// <inheritdoc/>
        [JsonIgnore]
        public bool HasCrossReferences => false;

        /// <inheritdoc/>
        [JsonIgnore]
        public List<IFileEventArgs.FileCrossReference> CrossReferences => new();
    }
}