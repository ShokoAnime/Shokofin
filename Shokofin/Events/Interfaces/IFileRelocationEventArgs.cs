
namespace Shokofin.Events.Interfaces;

public interface IFileRelocationEventArgs : IFileEventArgs {
    /// <summary>
    /// The ID of the previous managed folder the event was detected in.
    /// </summary>
    /// <value></value>
    int PreviousManagedFolderId { get; }

    /// <summary>
    /// The relative path from the previous base of the
    /// <see cref="ManagedFolder"/> to where the <see cref="File"/> previously
    /// lied, with a leading slash applied at the start.
    /// </summary>
    string PreviousRelativePath { get; }
}
