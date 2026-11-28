namespace Shokofin.Configuration;

/// <summary>
/// Third party providers to show external links for in the UI.
/// </summary>
/// <remarks>
/// Member names are persisted in the plugin configuration, so existing members
/// must not be renamed.
/// </remarks>
public enum ThirdPartyLinkProvider {
    /// <summary>
    /// Links to the Shoko Web UI.
    /// </summary>
    Shoko = 1,

    /// <summary>
    /// Links to AniDB.
    /// </summary>
    AniDB = 2,

    /// <summary>
    /// Links to TvDB.
    /// </summary>
    TvDB = 3,

    /// <summary>
    /// Links to TMDB.
    /// </summary>
    TMDB = 4,

    /// <summary>
    /// Links to AniList.
    /// </summary>
    AniList = 5,

    /// <summary>
    /// Links to MyAnimeList.
    /// </summary>
    MAL = 6,
}
