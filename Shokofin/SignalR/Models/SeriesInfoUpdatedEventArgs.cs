using System.Collections.Generic;
using System.Text.Json.Serialization;
using Jellyfin.Data.Enums;
using Shokofin.Events.Interfaces;

namespace Shokofin.SignalR.Models;

public class SeriesInfoUpdatedEventArgs : IMetadataUpdatedEventArgs
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
    public ProviderName ProviderName { get; set; } = ProviderName.None;

    /// <summary>
    /// The provided metadata series id.
    /// </summary>
    [JsonInclude, JsonPropertyName("SeriesID")]
    public int ProviderId { get; set; }

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

    #region IMetadataUpdatedEventArgs Impl.

    BaseItemKind IMetadataUpdatedEventArgs.Kind => BaseItemKind.Series;

    int? IMetadataUpdatedEventArgs.ProviderParentId => null;

    IReadOnlyList<int> IMetadataUpdatedEventArgs.EpisodeIds => new List<int>();

    IReadOnlyList<int> IMetadataUpdatedEventArgs.SeriesIds => SeriesIds;

    IReadOnlyList<int> IMetadataUpdatedEventArgs.GroupIds => GroupIds;

    #endregion
}