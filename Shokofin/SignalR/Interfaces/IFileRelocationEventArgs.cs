
namespace Shokofin.SignalR.Interfaces;

public interface IFileRelocationEventArgs
{
    /// <summary>
    /// Shoko file id.
    /// </summary>
    int FileId { get; }

    /// <summary>
    /// The ID of the new import folder the event was detected in.
    /// </summary>
    /// <value></value>
    int ImportFolderId { get; }

    /// <summary>
    /// The ID of the old import folder the event was detected in.
    /// </summary>
    /// <value></value>
    int PreviousImportFolderId { get; }

    /// <summary>
    /// The relative path of the new file from the import folder base location.
    /// </summary>
    string RelativePath { get; }

    /// <summary>
    /// The relative path of the old file from the import folder base location.
    /// </summary>
    string PreviousRelativePath { get; }
}