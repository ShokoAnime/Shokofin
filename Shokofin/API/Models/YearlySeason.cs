using System;
using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

public class YearlySeason : IComparable<YearlySeason>, IEquatable<YearlySeason> {
    /// <summary>
    /// The year of the season.
    /// </summary>
    [JsonPropertyName("Year")]
    public int Year { get; set; }

    /// <summary>
    /// The name of the season.
    /// </summary>
    [JsonPropertyName("AnimeSeason"), JsonConverter(typeof(JsonStringEnumConverter))]
    public YearlySeasonName Season { get; set; }

    public int CompareTo(YearlySeason? other)
    {
        if (other is null) return 1;
        var value = Year.CompareTo(other.Year);
        if (value == 0)
            value = Season.CompareTo(other.Season);
        return value;
    }

    public bool Equals(YearlySeason? other)
        => other is not null && Year == other.Year && Season == other.Season;

    public override bool Equals(object? obj)
        => Equals(obj as YearlySeason);

    public override int GetHashCode()
        => HashCode.Combine(Year, Season);
}
