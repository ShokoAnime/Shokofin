using System.Collections.Generic;
using System.Text.Json.Serialization;
using Shokofin.SignalR.Interfaces;

namespace Shokofin.SignalR.Models;

public class FileEventArgs : IFileEventArgs
{
    /// <summary>
    /// Shoko file id.
    /// </summary>
    [JsonPropertyName("FileID")]
    public int FileId { get; set; }

    /// <summary>
    /// The ID of the import folder the event was detected in.
    /// </summary>
    /// <value></value>
    [JsonPropertyName("ImportFolderID")]
    public int ImportFolderId { get; set; }

    /// <summary>
    /// The relative path of the file from the import folder base location.
    /// </summary>
    [JsonPropertyName("RelativePath")]
    public string RelativePath  { get; set; } = string.Empty;

    /// <summary>
    /// Cross references of episodes linked to this file.
    /// </summary>
    public List<IFileEventArgs.FileCrossReference> CrossReferences { get; set; } = new();
}
