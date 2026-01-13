using System.Collections.Generic;
using System.Text.Json.Serialization;
using Jellyfin.Data.Enums;
using Shokofin.API.Converters;
using Shokofin.Events.Interfaces;

namespace Shokofin.SignalR.Models;

public class MovieInfoUpdatedEventArgs : IMetadataUpdatedEventArgs {
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
    /// The provided metadata movie id.
    /// </summary>
    [JsonInclude, JsonPropertyName("MovieID"), JsonConverter(typeof(JsonAutoStringConverter))]
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// The provided metadata series id.
    /// </summary>
    [JsonInclude, JsonPropertyName("SeriesID")]
    public int ProviderParentId { get; set; }

    /// <summary>
    /// Shoko episode ids affected by this update.
    /// </summary>
    [JsonInclude, JsonPropertyName("ShokoEpisodeIDs")]
    public List<int> EpisodeIds { get; set; } = [];

    /// <summary>
    /// Shoko series ids affected by this update.
    /// </summary>
    [JsonInclude, JsonPropertyName("ShokoSeriesIDs")]
    public List<int> SeriesIds { get; set; } = [];

    #region IMetadataUpdatedEventArgs Impl.

    BaseItemKind IMetadataUpdatedEventArgs.Kind => BaseItemKind.Movie;

    int? IMetadataUpdatedEventArgs.ProviderParentId => ProviderParentId;

    IReadOnlyList<int> IMetadataUpdatedEventArgs.EpisodeIds => EpisodeIds;

    IReadOnlyList<int> IMetadataUpdatedEventArgs.SeriesIds => SeriesIds;

    #endregion
}