namespace Shokofin.API.Models;

public class Rating {
    /// <summary>
    /// The rating value relative to the <see cref="Rating.MaxValue"/>.
    /// </summary>
    public decimal Value { get; set; } = 0;

    /// <summary>
    /// Max value for the rating.
    /// </summary>
    public int MaxValue { get; set; } = 0;

    /// <summary>
    /// AniDB, etc.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// number of votes
    /// </summary>
    public int? Votes { get; set; }

    /// <summary>
    /// for temporary vs permanent, or any other situations that may arise later
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Json deserialization constructor.
    /// </summary>
    public Rating() { }

    /// <summary>
    /// Copy constructor.
    /// </summary>
    public Rating(Rating rating) {
        Value = rating.Value;
        MaxValue = rating.MaxValue;
        Source = rating.Source;
        Votes = rating.Votes;
        Type = rating.Type;
    }

    public float ToFloat(int scale)
        => scale == MaxValue ? (float)Value : (float)((Value * scale) / MaxValue);

}
