using System.Text.Json.Serialization;

namespace Shokofin.SignalR.Models;

public class SeriesInfoUpdatedEventArgs
{
    /// <summary>
    /// Shoko series id.
    /// </summary>
    [JsonPropertyName("SeriesID")]
    public int SeriesId { get; set; }

    /// <summary>
    /// Shoko group id.
    /// </summary>
    [JsonPropertyName("GroupID")]
    public int GroupId { get; set; }
}