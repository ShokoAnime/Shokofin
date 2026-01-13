using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

public class Text {
    /// <summary>
    /// The text value.
    /// </summary>
    [JsonPropertyName("Value")]
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Setter for titles.
    /// </summary>
    [JsonPropertyName("Name")]
    public string LegacyValue { set => Value = value; }

    /// <summary>
    /// alpha 3 language codes with custom extensions (e.g. "x-jat" for romaji, etc.).
    /// </summary>
    [JsonPropertyName("Language")]
    public string LanguageCode { get; set; } = "unk";

    /// <summary>
    /// True if this is the default text value among all values for the entity.
    /// </summary>
    [JsonPropertyName("Default")]
    public bool IsDefault { get; set; }

    /// <summary>
    /// True if this is the preferred text value among all values for the entity.
    /// </summary>
    [JsonPropertyName("Preferred")]
    public bool IsPreferred { get; set; }

    /// <summary>
    /// AniDB, TMDB, AniList, etc.
    /// </summary>
    [JsonPropertyName("Source")]
    public string Source { get; set; } = "Unknown";
}
