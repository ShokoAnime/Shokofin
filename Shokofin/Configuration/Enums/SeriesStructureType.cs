
namespace Shokofin.Configuration;

/// <summary>
/// Library structure type to use for series.
/// </summary>
public enum SeriesStructureType {
    /// <summary>
    /// Do not set the library structure.
    /// </summary>
    None,

    /// <summary>
    /// Structure the libraries as AniDB anime.
    /// </summary>
    AniDB_Anime,

    /// <summary>
    /// Structure the libraries using Shoko's group structure.
    /// </summary>
    Shoko_Groups,

    /// <summary>
    /// Structure the libraries as TMDB series and/or movies.
    /// </summary>
    TMDB_SeriesAndMovies,
}
