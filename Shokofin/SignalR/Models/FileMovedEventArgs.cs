using System.Text.Json.Serialization;
using Shokofin.SignalR.Interfaces;

namespace Shokofin.SignalR.Models;

public class FileMovedEventArgs : IFileRelocationEventArgs
{
    /// <summary>
    /// Shoko file id.
    /// </summary>
    [JsonPropertyName("FileID")]
    public int FileId { get; set; }

    /// <summary>
    /// The ID of the new import folder the event was detected in.
    /// </summary>
    /// <value></value>
    [JsonPropertyName("NewImportFolderID")]
    public int ImportFolderId { get; set; }

    /// <summary>
    /// The ID of the old import folder the event was detected in.
    /// </summary>
    /// <value></value>
    [JsonPropertyName("OldImportFolderID")]
    public int PreviousImportFolderId { get; set; }

    /// <summary>
    /// The relative path of the new file from the import folder base location.
    /// </summary>
    [JsonPropertyName("NewRelativePath")]
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// The relative path of the old file from the import folder base location.
    /// </summary>
    [JsonPropertyName("OldRelativePath")]
    public string PreviousRelativePath { get; set; } = string.Empty;
}
