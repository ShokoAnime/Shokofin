using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TitleType {
    None = 0,
    Main = 1,
    Official = 2,
    Short = 3,
    Synonym = 4,
    TitleCard = 5,
    KanjiReading = 6,
}
