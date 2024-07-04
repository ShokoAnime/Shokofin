namespace Shokofin.API.Models;

public class Rating
{
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

    public float ToFloat(uint scale = 1)
    {
        return (float)((Value * scale) / MaxValue);
    }
}
