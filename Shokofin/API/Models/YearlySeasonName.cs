
namespace Shokofin.API.Models;

/// <summary>
/// The name of a yearly season.
/// </summary>
public enum YearlySeasonName {
    /// <summary>
    /// Winter.
    /// </summary>
    Winter = 0,

    /// <summary>
    /// Spring.
    /// </summary>
    Spring = 1,

    /// <summary>
    /// Summer.
    /// </summary>
    Summer = 2,

    /// <summary>
    /// Autumn. 
    /// </summary>
    Autumn = 3,

    /// <summary>
    /// Fall. This is an alias for <see cref="Autumn"/>.
    /// </summary>
    Fall = Autumn,
}
