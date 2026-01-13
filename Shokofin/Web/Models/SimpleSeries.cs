
namespace Shokofin.Web.Models;

/// <summary>
/// A simple series model.
/// </summary>
public class SimpleSeries {
    /// <summary>
    /// Shoko Series ID.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// AniDB Anime ID.
    /// </summary>
    public required int AnidbId { get; init; }

    /// <summary>
    /// Preferred Title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Default Title.
    /// </summary>
    public required string DefaultTitle { get; init; }
}