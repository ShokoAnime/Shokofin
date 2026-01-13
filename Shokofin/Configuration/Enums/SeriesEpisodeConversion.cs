
namespace Shokofin.Configuration;

/// <summary>
/// Series episode conversion configuration.
/// </summary>
public enum SeriesEpisodeConversion {
    /// <summary>
    /// Do not convert episode types.
    /// </summary>
    None = 0,

    /// <summary>
    /// Convert normal episodes to specials.
    /// </summary>
    EpisodesAsSpecials = 1,

    /// <summary>
    /// Convert specials to normal episodes.
    /// </summary>
    SpecialsAsEpisodes = 2,

    /// <summary>
    /// Always convert specials to extra featurettes.
    /// </summary>
    SpecialsAsExtraFeaturettes = 3,
}
