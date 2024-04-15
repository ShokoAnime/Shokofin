using System.Collections.Generic;
using System.Text.Json.Serialization;
using Shokofin.SignalR.Interfaces;

namespace Shokofin.SignalR.Models;


public class FileMovedEventArgs: FileEventArgs, IFileRelocationEventArgs
{
    /// <inheritdoc/>
    [JsonInclude, JsonPropertyName("PreviousImportFolderID")]
    public int PreviousImportFolderId { get; set; }

    /// <summary>
    /// The previous relative path with no leading slash and directory
    /// seperators used on the Shoko side.
    /// </summary>
    [JsonInclude, JsonPropertyName("PreviousRelativePath")]
    private string PreviousInternalPath  { get; set; } = string.Empty;

    /// <summary>
    /// Cached path for later re-use.
    /// </summary>
    [JsonIgnore]
    private string? PreviousCachedPath { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public string PreviousRelativePath =>
        PreviousCachedPath ??= System.IO.Path.DirectorySeparatorChar + PreviousInternalPath
            .Replace('/', System.IO.Path.DirectorySeparatorChar)
            .Replace('\\', System.IO.Path.DirectorySeparatorChar);

    public class V0 : IFileRelocationEventArgs
    {
        /// <inheritdoc/>
        [JsonInclude, JsonPropertyName("FileID")]
        public int FileId { get; set; }

        /// <inheritdoc/>
        [JsonInclude, JsonPropertyName("NewImportFolderID")]
        public int ImportFolderId { get; set; }

        /// <inheritdoc/>
        [JsonInclude, JsonPropertyName("OldImportFolderID")]
        public int PreviousImportFolderId { get; set; }

        /// <summary>
        /// The relative path with no leading slash and directory seperators used on
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
        public string RelativePath =>
            CachedPath ??= System.IO.Path.DirectorySeparatorChar + InternalPath
                .Replace('/', System.IO.Path.DirectorySeparatorChar)
                .Replace('\\', System.IO.Path.DirectorySeparatorChar);


        /// <summary>
        /// The previous relative path with no leading slash and directory
        /// seperators used on the Shoko side.
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
        public string PreviousRelativePath =>
            PreviousCachedPath ??= System.IO.Path.DirectorySeparatorChar + PreviousInternalPath
                .Replace('/', System.IO.Path.DirectorySeparatorChar)
                .Replace('\\', System.IO.Path.DirectorySeparatorChar);

        /// <inheritdoc/>
        [JsonIgnore]
        public List<IFileEventArgs.FileCrossReference> CrossReferences => new();
    }
}
