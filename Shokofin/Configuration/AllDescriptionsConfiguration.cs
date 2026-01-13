
using DescriptionProvider = Shokofin.Utils.TextUtility.DescriptionProvider;

namespace Shokofin.Configuration;

/// <summary>
/// All description configurations, with support for per structure type per
/// base item type configuration.
/// </summary>
public class AllDescriptionsConfiguration {
    /// <summary>
    /// Default description settings.
    /// </summary>
    public DescriptionConfiguration Default { get; set; } = new() {
        List = [DescriptionProvider.Shoko],
    };

    /// <summary>
    /// Description settings for Shoko collections.
    /// </summary>
    public ToggleDescriptionConfiguration ShokoCollection { get; set; } = new();

    /// <summary>
    /// Description settings for TMDb collections.
    /// </summary>
    public ToggleDescriptionConfiguration TmdbCollection { get; set; } = new();

    /// <summary>
    /// Description settings for AniDB movies.
    /// </summary>
    public ToggleDescriptionConfiguration AnidbMovie { get; set; } = new();

    /// <summary>
    /// Description settings for Shoko movies.
    /// </summary>
    public ToggleDescriptionConfiguration ShokoMovie { get; set; } = new();

    /// <summary>
    /// Description settings for TMDb movies.
    /// </summary>
    public ToggleDescriptionConfiguration TmdbMovie { get; set; } = new();

    /// <summary>
    /// Description settings for AniDB anime.
    /// </summary>
    public ToggleDescriptionConfiguration AnidbAnime { get; set; } = new();

    /// <summary>
    /// Description settings for Shoko series.
    /// </summary>
    public ToggleDescriptionConfiguration ShokoSeries { get; set; } = new();

    /// <summary>
    /// Description settings for TMDb shows.
    /// </summary>
    public ToggleDescriptionConfiguration TmdbShow { get; set; } = new();

    /// <summary>
    /// Description settings for AniDB seasons.
    /// </summary>
    public ToggleDescriptionConfiguration AnidbSeason { get; set; } = new();

    /// <summary>
    /// Description settings for Shoko seasons.
    /// </summary>
    public ToggleDescriptionConfiguration ShokoSeason { get; set; } = new();

    /// <summary>
    /// Description settings for TMDb seasons.
    /// </summary>
    public ToggleDescriptionConfiguration TmdbSeason { get; set; } = new();

    /// <summary>
    /// Description settings for AniDB episodes.
    /// </summary>
    public ToggleDescriptionConfiguration AnidbEpisode { get; set; } = new();

    /// <summary>
    /// Description settings for Shoko episodes.
    /// </summary>
    public ToggleDescriptionConfiguration ShokoEpisode { get; set; } = new();

    /// <summary>
    /// Description settings for TMDb episodes.
    /// </summary>
    public ToggleDescriptionConfiguration TmdbEpisode { get; set; } = new();
}
