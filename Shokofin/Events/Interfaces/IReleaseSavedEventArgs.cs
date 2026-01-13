
namespace Shokofin.Events.Interfaces;

public interface IReleaseSavedEventArgs {
    /// <summary>
    /// Shoko file id.
    /// </summary>
    int FileId { get; }
}
