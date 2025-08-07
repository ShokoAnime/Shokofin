
namespace Shokofin.Configuration;

/// <summary>
/// All image configurations, with support for per structure type per
/// base item type configuration.
/// </summary>
public class AllImagesConfiguration {
    /// <summary>
    /// Enable debug mode for images.
    /// </summary>
    public bool DebugMode { get; set; }

    /// <summary>
    /// Default image settings.
    /// </summary>
    public ImageConfiguration Default { get; set; } = new() {
        UsePreferred = true,
    };

    /// <summary>
    /// Image settings for Shoko collections.
    /// </summary>
    public ToggleImageConfiguration ShokoCollection { get; set; } = new();

    /// <summary>
    /// Image settings for TMDb collections.
    /// </summary>
    public ToggleImageConfiguration TmdbCollection { get; set; } = new();

    /// <summary>
    /// Image settings for AniDB movies.
    /// </summary>
    public ToggleImageConfiguration AnidbMovie { get; set; } = new();

    /// <summary>
    /// Image settings for Shoko movies.
    /// </summary>
    public ToggleImageConfiguration ShokoMovie { get; set; } = new();

    /// <summary>
    /// Image settings for TMDb movies.
    /// </summary>
    public ToggleImageConfiguration TmdbMovie { get; set; } = new();

    /// <summary>
    /// Image settings for AniDB anime.
    /// </summary>
    public ToggleImageConfiguration AnidbAnime { get; set; } = new();

    /// <summary>
    /// Image settings for Shoko series.
    /// </summary>
    public ToggleImageConfiguration ShokoSeries { get; set; } = new();

    /// <summary>
    /// Image settings for TMDb shows.
    /// </summary>
    public ToggleImageConfiguration TmdbShow { get; set; } = new();

    /// <summary>
    /// Image settings for AniDB seasons.
    /// </summary>
    public ToggleImageConfiguration AnidbSeason { get; set; } = new();

    /// <summary>
    /// Image settings for Shoko seasons.
    /// </summary>
    public ToggleImageConfiguration ShokoSeason { get; set; } = new();

    /// <summary>
    /// Image settings for TMDb seasons.
    /// </summary>
    public ToggleImageConfiguration TmdbSeason { get; set; } = new();

    /// <summary>
    /// Image settings for AniDB episodes.
    /// </summary>
    public ToggleImageConfiguration AnidbEpisode { get; set; } = new();

    /// <summary>
    /// Image settings for Shoko episodes.
    /// </summary>
    public ToggleImageConfiguration ShokoEpisode { get; set; } = new();

    /// <summary>
    /// Image settings for TMDb episodes.
    /// </summary>
    public ToggleImageConfiguration TmdbEpisode { get; set; } = new();
}
