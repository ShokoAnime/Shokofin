using System.Collections.Generic;
using System.Text.Json.Serialization;
using Shokofin.SignalR.Interfaces;

namespace Shokofin.SignalR.Models;


public class FileRenamedEventArgs : FileEventArgs, IFileRelocationEventArgs
{
    /// <summary>
    /// The new File name.
    /// </summary>
    [JsonInclude, JsonPropertyName("FileName")]
    public string FileName  { get; set; } = string.Empty;

    /// <summary>
    /// The old file name.
    /// </summary>
    [JsonInclude, JsonPropertyName("PreviousFileName")]
    public string PreviousFileName  { get; set; } = string.Empty;

    /// <inheritdoc/>
    [JsonIgnore]
    public int PreviousImportFolderId => ImportFolderId;

    /// <inheritdoc/>
    [JsonIgnore]
    public string PreviousRelativePath => RelativePath[^FileName.Length] + PreviousFileName;

    public class V0 : IFileRelocationEventArgs
    {
        /// <inheritdoc/>
        [JsonInclude, JsonPropertyName("FileID")]
        public int FileId { get; set; }

        /// <inheritdoc/>
        [JsonInclude, JsonPropertyName("ImportFolderID")]
        public int ImportFolderId { get; set; }

        /// <summary>
        /// The relative path with no leading slash and directory seperators used on
        /// the Shoko side.
        /// </summary>
        [JsonInclude, JsonPropertyName("RelativePath")]
        private string InternalPath  { get; set; } = string.Empty;

        /// <summary>
        /// Cached path for later re-use.
        /// </summary>
        [JsonIgnore]
        private string? CachedPath { get; set; }

        /// <inheritdoc/>
        [JsonIgnore]
        public string RelativePath =>
            CachedPath ??= System.IO.Path.DirectorySeparatorChar + InternalPath
                .Replace('/', System.IO.Path.DirectorySeparatorChar)
                .Replace('\\', System.IO.Path.DirectorySeparatorChar);

        /// <summary>
        /// The new File name.
        /// </summary>
        [JsonInclude, JsonPropertyName("NewFileName")]
        public string FileName  { get; set; } = string.Empty;

        /// <summary>
        /// The old file name.
        /// </summary>
        [JsonInclude, JsonPropertyName("OldFileName")]
        public string PreviousFileName  { get; set; } = string.Empty;


        /// <inheritdoc/>
        [JsonIgnore]
        public int PreviousImportFolderId => ImportFolderId;

        /// <inheritdoc/>
        [JsonIgnore]
        public string PreviousRelativePath => RelativePath[^FileName.Length] + PreviousFileName;

        /// <inheritdoc/>
        [JsonIgnore]
        public List<IFileEventArgs.FileCrossReference> CrossReferences => new();
    }
}