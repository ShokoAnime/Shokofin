using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Shokofin.SignalR.Models;

public class EpisodeInfoUpdatedEventArgs
{
    /// <summary>
    /// The update reason.
    /// </summary>
    [JsonInclude, JsonPropertyName("Reason")]
    public UpdateReason Reason { get; set; }

    /// <summary>
    /// The provider metadata source.
    /// </summary>
    [JsonInclude, JsonPropertyName("Source")]
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// The provided metadata episode id.
    /// </summary>
    [JsonInclude, JsonPropertyName("EpisodeID")]
    public int ProviderId { get; set; }

    /// <summary>
    /// The provided metadata series id.
    /// </summary>
    [JsonInclude, JsonPropertyName("SeriesID")]
    public int ProviderSeriesId { get; set; }

    /// <summary>
    /// Shoko episode ids affected by this update.
    /// </summary>
    [JsonInclude, JsonPropertyName("ShokoEpisodeIDs")]
    public List<int> EpisodeIds { get; set; } = new();

    /// <summary>
    /// Shoko series ids affected by this update.
    /// </summary>
    [JsonInclude, JsonPropertyName("ShokoSeriesIDs")]
    public List<int> SeriesIds { get; set; } = new();

    /// <summary>
    /// Shoko group ids affected by this update.
    /// </summary>
    [JsonInclude, JsonPropertyName("ShokoGroupIDs")]
    public List<int> GroupIds { get; set; } = new();
}