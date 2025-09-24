using TitleProvider = Shokofin.Utils.TextUtility.TitleProvider;

namespace Shokofin.Configuration;

/// <summary>
/// Advanced title configuration with support for per structure type per base
/// item type configuration.
/// </summary>
public class AllTitlesConfiguration {
    /// <summary>
    /// Default title settings.
    /// </summary>
    public TitlesConfiguration Default { get; set; } = new() {
        MainTitle = new() {
            List = [TitleProvider.Shoko_Default],
        },
    };

    /// <summary>
    /// Title settings for Shoko collections.
    /// </summary>
    public ToggleTitlesConfiguration ShokoCollection { get; set; } = new();

    /// <summary>
    /// Title settings for TMDb collections.
    /// </summary>
    public ToggleTitlesConfiguration TmdbCollection { get; set; } = new();

    /// <summary>
    /// Title settings for AniDB movies.
    /// </summary>
    public ToggleTitlesConfiguration AnidbMovie { get; set; } = new();

    /// <summary>
    /// Title settings for Shoko movies.
    /// </summary>
    public ToggleTitlesConfiguration ShokoMovie { get; set; } = new();

    /// <summary>
    /// Title settings for TMDb movies.
    /// </summary>
    public ToggleTitlesConfiguration TmdbMovie { get; set; } = new();

    /// <summary>
    /// Title settings for AniDB anime.
    /// </summary>
    public ToggleTitlesConfiguration AnidbAnime { get; set; } = new();

    /// <summary>
    /// Title settings for Shoko series.
    /// </summary>
    public ToggleTitlesConfiguration ShokoSeries { get; set; } = new();

    /// <summary>
    /// Title settings for TMDb shows.
    /// </summary>
    public ToggleTitlesConfiguration TmdbShow { get; set; } = new();

    /// <summary>
    /// Title settings for AniDB seasons.
    /// </summary>
    public ToggleTitlesConfiguration AnidbSeason { get; set; } = new();

    /// <summary>
    /// Title settings for Shoko seasons.
    /// </summary>
    public ToggleTitlesConfiguration ShokoSeason { get; set; } = new();

    /// <summary>
    /// Title settings for TMDb seasons.
    /// </summary>
    public ToggleTitlesConfiguration TmdbSeason { get; set; } = new();

    /// <summary>
    /// Title settings for AniDB episodes.
    /// </summary>
    public ToggleTitlesConfiguration AnidbEpisode { get; set; } = new();

    /// <summary>
    /// Title settings for Shoko episodes.
    /// </summary>
    public ToggleTitlesConfiguration ShokoEpisode { get; set; } = new();

    /// <summary>
    /// Title settings for TMDb episodes.
    /// </summary>
    public ToggleTitlesConfiguration TmdbEpisode { get; set; } = new();
}