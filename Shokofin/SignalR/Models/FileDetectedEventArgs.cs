using System.Text.Json.Serialization;

#nullable enable
namespace Shokofin.SignalR.Models;

public class FileDetectedEventArgs
{
    /// <summary>
    /// The ID of the import folder the event was detected in.
    /// </summary>
    [JsonPropertyName("ImportFolderID")]
    public int ImportFolderId { get; set; }

    /// <summary>
    /// The relative path of the file from the import folder base location
    /// </summary>
    [JsonPropertyName("RelativePath")]
    public string RelativePath  { get; set; } = string.Empty;
}
