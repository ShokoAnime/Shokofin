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
    [JsonInclude, JsonPropertyName("FileLocationID")]
    public int? FileLocationId { get; set; }

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

    /// <inheritdoc/>
    [JsonIgnore]
    public bool HasCrossReferences { get; set; } = false;

    /// <inheritdoc/>
    [JsonIgnore]
    public List<IFileEventArgs.FileCrossReference> CrossReferences { get; set; } = new();

#pragma warning disable IDE0051
    /// <summary>
    /// Current cross-references of episodes linked to this file. Only present
    /// for setting the cross-references when deserializing JSON.
    /// </summary>
    [JsonInclude, JsonPropertyName("CrossReferences")]
    public List<IFileEventArgs.FileCrossReference> CurrentCrossReferences { get => CrossReferences; set { HasCrossReferences = true; CrossReferences = value; } }

    /// <summary>
    /// Legacy cross-references of episodes linked to this file. Only present
    /// for setting the cross-references when deserializing JSON.
    /// </summary>
    [JsonInclude, JsonPropertyName("CrossRefs")]
    public List<IFileEventArgs.FileCrossReference> LegacyCrossReferences { get => CrossReferences; set { HasCrossReferences = true; CrossReferences = value; } }
#pragma warning restore IDE0051
}
