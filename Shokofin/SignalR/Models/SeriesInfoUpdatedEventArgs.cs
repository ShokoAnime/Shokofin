using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Shokofin.SignalR.Models;

public class SeriesInfoUpdatedEventArgs
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
    /// The provided metadata series id.
    /// </summary>
    [JsonPropertyName("SeriesID")]
    public int ProviderId { get; set; }

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