using System;

namespace Shokofin.Configuration;

/// <summary>
/// Determines which metadata fields to update.
/// </summary>
[Flags]
public enum MetadataRefreshField : ulong {
    /// <summary>
    /// Will not update any metadata.
    /// </summary>
    None = 0,

    /// <summary>
    /// Will update the titles and overview.
    /// </summary>
    TitlesAndOverview = 1 << 0,

    /// <summary>
    /// Will update the premiere date, production year, end date, air status,
    /// and runtime.
    /// </summary>
    Dates = 1 << 1,

    /// <summary>
    /// Will update the tags and genres.
    /// </summary>
    TagsAndGenres = 1 << 2,

    /// <summary>
    /// Will update the studios and production locations.
    /// </summary>
    StudiosAndProductionLocations = 1 << 3,

    /// <summary>
    /// Will update the cast and crew.
    /// </summary>
    CastAndCrew = 1 << 4,

    /// <summary>
    /// Will update the official rating, community rating and custom rating.
    /// </summary>
    ContentRatings = 1 << 5,

    /// <summary>
    /// Will update all images.
    /// </summary>
    Images = 1 << 6,

    /// <summary>
    /// Will set the preferred images to match what is set in Shoko.
    /// </summary>
    PreferredImages = 1 << 7,

    /// <summary>
    /// Will update all child metadata recursively.
    /// </summary>
    Recursive = 1 << 8,
    
    /// <summary>
    /// Will update all owned metadata.
    /// </summary>
    OwnedItems = 1 << 9,

    /// <summary>
    /// Will update the plugin managed provider ids.
    /// </summary>
    ProviderIds = 1 << 10,

    /// <summary>
    /// Will run the custom provider.
    /// </summary>
    CustomProvider = 1L << 30,

    /// <summary>
    /// Will use the legacy refresh behavior.
    /// </summary>
    LegacyRefresh = 1L << 31,
}
