using System.Text.Json.Serialization;

namespace Shokofin.SignalR.Models;

public class FileEventArgs
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
}
