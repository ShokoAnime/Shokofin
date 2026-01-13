using System.Collections.Generic;
using System.Text.Json.Serialization;
using Jellyfin.Data.Enums;
using Shokofin.API.Converters;
using Shokofin.Events.Interfaces;

namespace Shokofin.SignalR.Models;

public class SeriesInfoUpdatedEventArgs : IMetadataUpdatedEventArgs {
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
    [JsonInclude, JsonPropertyName("SeriesID"), JsonConverter(typeof(JsonAutoStringConverter))]
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Shoko series ids affected by this update.
    /// </summary>
    [JsonInclude, JsonPropertyName("ShokoSeriesIDs")]
    public List<int> SeriesIds { get; set; } = [];

    #region IMetadataUpdatedEventArgs Impl.

    BaseItemKind IMetadataUpdatedEventArgs.Kind => BaseItemKind.Series;

    int? IMetadataUpdatedEventArgs.ProviderParentId => null;

    IReadOnlyList<int> IMetadataUpdatedEventArgs.EpisodeIds => [];

    IReadOnlyList<int> IMetadataUpdatedEventArgs.SeriesIds => SeriesIds;

    #endregion
}