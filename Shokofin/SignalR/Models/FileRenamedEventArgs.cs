using System.Text.Json.Serialization;
using Shokofin.SignalR.Interfaces;

#nullable enable
namespace Shokofin.SignalR.Models;

public class FileRenamedEventArgs : FileEventArgs, IFileRelocationEventArgs
{
    /// <summary>
    /// The new File name.
    /// </summary>
    [JsonPropertyName("NewFileName")]
    public string FileName  { get; set; } = string.Empty;

    /// <summary>
    /// The old file name.
    /// </summary>
    [JsonPropertyName("OldFileName")]
    public string PreviousFileName  { get; set; } = string.Empty;

    public int PreviousImportFolderId { get; set; }

    /// <summary>
    /// The relative path of the old file from the import folder base location.
    /// </summary>
    public string PreviousRelativePath => RelativePath[^FileName.Length] + PreviousFileName;
}