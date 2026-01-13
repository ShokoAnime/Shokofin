using System.Text.Json.Serialization;
using Shokofin.Events.Interfaces;

namespace Shokofin.SignalR.Models;

public class FileMovedEventArgs: FileEventArgs, IFileRelocationEventArgs {
    /// <inheritdoc/>
    [JsonInclude, JsonPropertyName("PreviousImportFolderID")]
    public int PreviousImportFolderId {
        get => PreviousManagedFolderId;
        set => PreviousManagedFolderId = value;
    }

    /// <inheritdoc/>
    [JsonInclude, JsonPropertyName("PreviousManagedFolderID")]
    public int PreviousManagedFolderId { get; set; }

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
    public string PreviousRelativePath {
        get {
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
}
