using System.Text.Json.Serialization;

namespace Shokofin.SignalR.Interfaces;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProviderName
{
    None = 0,
    Shoko = 1,
    AniDB = 2,
    TMDB = 3,
}