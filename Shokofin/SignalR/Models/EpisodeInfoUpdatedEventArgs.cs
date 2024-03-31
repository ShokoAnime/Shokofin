using System.Text.Json.Serialization;

#nullable enable
namespace Shokofin.SignalR.Models;

public class EpisodeInfoUpdatedEventArgs
{
    /// <summary>
    /// Shoko episode id.
    /// </summary>
    [JsonPropertyName("EpisodeID")]
    public int EpisodeId { get; set; }

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