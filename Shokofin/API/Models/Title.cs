using System.Text.Json.Serialization;

#nullable enable
namespace Shokofin.API.Models;

public class Title
{
    /// <summary>
    /// The title.
    /// </summary>
    [JsonPropertyName("Name")]
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// 3-digit language code (x-jat, etc. are exceptions)
    /// </summary>
    [JsonPropertyName("Language")]
    public string LanguageCode { get; set; } = "unk";
    /// <summary>
    /// AniDB series type. Only available on series titles.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TitleType? Type { get; set; }

    /// <summary>
    /// True if this is the default title for the entry.
    /// </summary>
    [JsonPropertyName("Default")]
    public bool IsDefault { get; set; }

    /// <summary>
    /// AniDB, TvDB, AniList, etc.
    /// </summary>
    public string Source { get; set; } = "Unknown";
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TitleType
{
    None = 0,
    Main = 1,
    Official = 2,
    Short = 3,
    Synonym = 4,
    TitleCard = 5,
    KanjiReading = 6,
}