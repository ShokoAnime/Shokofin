using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

public class Title : Text {
    /// <summary>
    /// AniDB anime title type. Only available on series level titles.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TitleType? Type { get; set; }
}
