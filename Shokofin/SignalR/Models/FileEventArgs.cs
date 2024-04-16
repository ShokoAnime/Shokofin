using System.Collections.Generic;
using System.Text.Json.Serialization;
using Shokofin.SignalR.Interfaces;

namespace Shokofin.SignalR.Models;

public class FileEventArgs : IFileEventArgs
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

    /// <inheritdoc/>
    [JsonInclude, JsonPropertyName("CrossReferences")]
    public List<IFileEventArgs.FileCrossReference> CrossReferences { get; set; } = new();

#pragma warning disable IDE0051
    /// <summary>
    /// Legacy cross-references of episodes linked to this file. Only present
    /// for setting the cross-references when deserializing JSON.
    /// </summary>
    [JsonInclude, JsonPropertyName("CrossRefs")]
    public List<IFileEventArgs.FileCrossReference> LegacyCrossReferences { set { CrossReferences = value; } }
#pragma warning restore IDE0051
}
