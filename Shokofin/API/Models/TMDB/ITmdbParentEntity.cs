using System.Collections.Generic;

namespace Shokofin.API.Models.TMDB;

public interface ITmdbParentEntity : ITmdbEntity {
    /// <summary>
    /// Original language the entity was shot in.
    /// /// </summary>
    string OriginalLanguage { get; }

    /// <summary>
    /// Indicates the entity is restricted to an age group above the legal age,
    /// because it's a pornography.
    /// </summary>
    bool IsRestricted { get; }

    /// <summary>
    /// Genres.
    /// </summary>
    IReadOnlyList<string> Genres { get; }

    /// <summary>
    /// Keywords.
    /// </summary>
    IReadOnlyList<string> Keywords { get; }

    /// <summary>
    /// User rating of the entity from TMDB users.
    /// </summary>
    Rating UserRating { get; }

    /// <summary>
    /// Content ratings for different countries for this entity.
    /// </summary>
    IReadOnlyList<ContentRating> ContentRatings { get; }

    /// <summary>
    /// The production countries.
    /// </summary>
    IReadOnlyDictionary<string, string> ProductionCountries { get; }

    /// <summary>
    /// The production companies (studios) that produced the entity.
    /// </summary>
    IReadOnlyList<Studio> Studios { get; }
}
