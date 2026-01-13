using System;

namespace Shokofin.API.Models;

public class ContentRating : IEquatable<ContentRating> {
    /// <summary>
    /// The content rating for the specified language.
    /// </summary>
    public string Rating { get; set; } = string.Empty;

    /// <summary>
    /// The country code the rating applies for.
    /// </summary>
    public string Country { get; set; } = string.Empty;

    /// <summary>
    /// The language code the rating applies for.
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// The source of the content rating.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    public bool Equals(ContentRating? other)
        => other is not null && (
            ReferenceEquals(this, other) || (
                Rating == other.Rating &&
                Country == other.Country &&
                Language == other.Language &&
                Source == other.Source
            )
        );

    public override bool Equals(object? obj)
        => obj is ContentRating other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine( Rating, Country, Language, Source);
}
