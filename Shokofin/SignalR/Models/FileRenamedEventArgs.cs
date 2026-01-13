using System.Text.Json.Serialization;
using Shokofin.Events.Interfaces;

namespace Shokofin.SignalR.Models;

public class FileRenamedEventArgs : FileEventArgs, IFileRelocationEventArgs {
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
    public int PreviousManagedFolderId => ManagedFolderId;

    /// <inheritdoc/>
    [JsonIgnore]
    public string PreviousRelativePath => RelativePath[..^FileName.Length] + PreviousFileName;
}
