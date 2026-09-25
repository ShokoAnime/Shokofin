using System.Collections.Generic;
using System.Text.Json.Serialization;
using Jellyfin.Data.Enums;
using Shokofin.API.Converters;
using Shokofin.Events.Interfaces;

namespace Shokofin.SignalR.Models;

public class EpisodeInfoUpdatedEventArgs : IMetadataUpdatedEventArgs {
    /// <summary>
    /// The update reason.
    /// </summary>
    [JsonInclude, JsonPropertyName("Reason")]
    public UpdateReason Reason { get; set; }

    /// <summary>
    /// The provider metadata source.
    /// </summary>
    [JsonInclude, JsonPropertyName("Source"), JsonConverter(typeof(JsonProviderNameConverter))]
    public ProviderName ProviderName { get; set; } = ProviderName.None;

    /// <summary>
    /// The provided metadata episode id.
    /// </summary>
    [JsonInclude, JsonPropertyName("EpisodeID"), JsonConverter(typeof(JsonAutoStringConverter))]
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// The provided metadata series id.
    /// </summary>
    [JsonInclude, JsonPropertyName("SeriesID"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int ProviderParentId { get; set; }

    /// <summary>
    /// Shoko episode ids affected by this update.
    /// </summary>
    [JsonInclude, JsonPropertyName("ShokoEpisodeIDs"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public List<int> EpisodeIds { get; set; } = [];

    /// <summary>
    /// Shoko series ids affected by this update.
    /// </summary>
    [JsonInclude, JsonPropertyName("ShokoSeriesIDs"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public List<int> SeriesIds { get; set; } = [];

    #region IMetadataUpdatedEventArgs Impl.

    BaseItemKind IMetadataUpdatedEventArgs.Kind => BaseItemKind.Episode;

    int? IMetadataUpdatedEventArgs.ProviderParentId => ProviderParentId;

    IReadOnlyList<int> IMetadataUpdatedEventArgs.EpisodeIds => EpisodeIds;

    IReadOnlyList<int> IMetadataUpdatedEventArgs.SeriesIds => SeriesIds;

    #endregion
}