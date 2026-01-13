using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;

namespace Shokofin.API.Models.TMDB;

public interface ITmdbEntity {
    string Id { get; }

    BaseItemKind Kind { get; }

    /// <summary>
    /// Preferred title based upon Shoko's title preference.
    /// </summary>
    string Title { get; }

    /// <summary>
    /// All available titles for the entity, if they should be included.
    /// </summary>
    IReadOnlyList<Title> Titles { get; }

    /// <summary>
    /// Preferred overview based upon description preference.
    /// </summary>
    string Overview { get; }

    /// <summary>
    /// All available overviews for the entity, if they should be included.
    /// </summary>
    IReadOnlyList<Text> Overviews { get; }

    /// <summary>
    /// When the local metadata was first created.
    /// </summary>
    DateTime CreatedAt { get; }

    /// <summary>
    /// When the local metadata was last updated with new changes from the
    /// remote.
    /// </summary>
    DateTime LastUpdatedAt { get; }
}
