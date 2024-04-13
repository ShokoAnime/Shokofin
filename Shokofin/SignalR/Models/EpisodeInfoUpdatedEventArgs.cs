using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Shokofin.SignalR.Models;

public class EpisodeInfoUpdatedEventArgs
{
    /// <summary>
    /// The update reason.
    /// </summary>
    public UpdateReason Reason { get; set; }
    /// <summary>
    /// The provider metadata source.
    /// </summary>
    [JsonPropertyName("Source")]
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// The provided metadata episode id.
    /// </summary>
    [JsonPropertyName("EpisodeID")]
    public int ProviderId { get; set; }

    /// <summary>
    /// The provided metadata series id.
    /// </summary>
    [JsonPropertyName("SeriesID")]
    public int ProviderSeriesId { get; set; }

    /// <summary>
    /// Shoko episode ids affected by this update.
    /// </summary>
    [JsonPropertyName("ShokoEpisodeIDs")]
    public List<int> EpisodeIds { get; set; } = new();

    /// <summary>
    /// Shoko series ids affected by this update.
    /// </summary>
    [JsonPropertyName("ShokoSeriesIDs")]
    public List<int> SeriesIds { get; set; } = new();

    /// <summary>
    /// Shoko group ids affected by this update.
    /// </summary>
    [JsonPropertyName("ShokoGroupIDs")]
    public List<int> GroupIds { get; set; } = new();
}