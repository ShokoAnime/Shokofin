using System.Collections.Generic;
using System.Linq;

namespace Shokofin.Configuration;

public class ImageConfiguration {
    /// <summary>
    /// Indicates we should respect the preferred image flag sent by the server
    /// when selecting the images to use for an item.
    /// </summary>
    public bool UsePreferred { get; set; } = false;

    /// <summary>
    /// Indicates that we should use the community ratings to order the images
    /// for an item.
    /// </summary>
    public bool UseCommunityRating { get; set; } = false;

    /// <summary>
    /// Indicates that we should set the dimensions for the images when
    /// selecting the images to use for an item.
    /// </summary>
    public bool UseDimensions { get; set; } = false;

    /// <summary>
    /// The enabled image types for posters.
    /// </summary>
    public ImageLanguageType[] PosterList { get; set; } = [];

    /// <summary>
    /// The order of the enabled image types for posters.
    /// </summary>
    public ImageLanguageType[] PosterOrder { get; set; } = [
        ImageLanguageType.None,
        ImageLanguageType.Metadata,
        ImageLanguageType.Original,
        ImageLanguageType.English,
    ];

    /// <summary>
    /// The enabled image types for logos.
    /// </summary>
    public ImageLanguageType[] LogoList { get; set; } = [];

    /// <summary>
    /// The order of the enabled image types for logos.
    /// </summary>
    public ImageLanguageType[] LogoOrder { get; set; } = [
        ImageLanguageType.None,
        ImageLanguageType.Metadata,
        ImageLanguageType.Original,
        ImageLanguageType.English,
    ];

    /// <summary>
    /// The enabled image types for backdrops/banners/thumbnails.
    /// </summary>
    public ImageLanguageType[] BackdropList { get; set; } = [];

    /// <summary>
    /// The order of the enabled image types for backdrops/banners/thumbnails.
    /// </summary>
    public ImageLanguageType[] BackdropOrder { get; set; } = [
        ImageLanguageType.None,
        ImageLanguageType.Metadata,
        ImageLanguageType.Original,
        ImageLanguageType.English,
    ];

    /// <summary>
    /// Returns an ordered list of which image types to include for posters.
    /// </summary>
    public IReadOnlyList<ImageLanguageType> GetOrderedPosterTypes()
        => PosterOrder.Where((t) => t is not ImageLanguageType.Unknown && PosterList.Contains(t)).ToList();

    /// <summary>
    /// Returns an ordered list of which image types to include for logos.
    /// </summary>
    public IReadOnlyList<ImageLanguageType> GetOrderedLogoTypes()
        => LogoOrder.Where((t) => t is not ImageLanguageType.Unknown && LogoList.Contains(t)).ToList();

    /// <summary>
    /// Returns an ordered list of which image types to include for backdrops/banners/thumbnails.
    /// </summary>
    public IReadOnlyList<ImageLanguageType> GetOrderedBackdropTypes()
        => BackdropOrder.Where((t) => t is not ImageLanguageType.Unknown && BackdropList.Contains(t)).ToList();
}

public class ToggleImageConfiguration : ImageConfiguration {
    /// <summary>
    /// Whether or not the image configuration is enabled.
    /// </summary>
    public bool Enabled { get; set; }
}
