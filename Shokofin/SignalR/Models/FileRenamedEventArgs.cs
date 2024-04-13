using System.Text.Json.Serialization;
using Shokofin.SignalR.Interfaces;

namespace Shokofin.SignalR.Models;

public class FileRenamedEventArgsV1 : IFileRelocationEventArgs
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
    /// The new File name.
    /// </summary>
    [JsonPropertyName("NewFileName")]
    public string FileName  { get; set; } = string.Empty;

    /// <summary>
    /// The old file name.
    /// </summary>
    [JsonPropertyName("OldFileName")]
    public string PreviousFileName  { get; set; } = string.Empty;


    /// <inheritdoc/>
    public int PreviousImportFolderId => ImportFolderId;

    /// <inheritdoc/>
    public string PreviousRelativePath => RelativePath[^FileName.Length] + PreviousFileName;
}

public class FileRenamedEventArgs : FileEventArgs, IFileRelocationEventArgs
{
    /// <summary>
    /// The new File name.
    /// </summary>
    public string FileName  { get; set; } = string.Empty;

    /// <summary>
    /// The old file name.
    /// </summary>
    public string PreviousFileName  { get; set; } = string.Empty;

    /// <inheritdoc/>
    public int PreviousImportFolderId => ImportFolderId;

    /// <inheritdoc/>
    public string PreviousRelativePath => RelativePath[^FileName.Length] + PreviousFileName;
}