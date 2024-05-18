using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;

namespace Shokofin.SignalR.Interfaces;

public interface IMetadataUpdatedEventArgs
{
    /// <summary>
    /// The update reason.
    /// </summary>
    UpdateReason Reason { get; }

    /// <summary>
    /// The provider metadata type.
    /// </summary>
    BaseItemKind Kind { get; }

    /// <summary>
    /// The provider metadata source.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// The provided metadata episode id.
    /// </summary>
    int ProviderId { get; }

    /// <summary>
    /// Provider unique id.
    /// </summary>
    string ProviderUId => $"{ProviderName.ToLowerInvariant()}:{ProviderId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// The provided metadata series id.
    /// </summary>
    int? ProviderParentId { get; }

    /// <summary>
    /// Provider unique parent id.
    /// </summary>
    string? ProviderParentUId => ProviderParentId.HasValue ? $"{ProviderName.ToLowerInvariant()}:{ProviderParentId.Value.ToString(CultureInfo.InvariantCulture)}" : null;

    /// <summary>
    /// The first shoko episode id affected by this update.
    /// </summary>
    int? EpisodeId => EpisodeIds.Count > 0 ? EpisodeIds[0] : null;

    /// <summary>
    /// Shoko episode ids affected by this update.
    /// </summary>
    IReadOnlyList<int> EpisodeIds { get; }

    /// <summary>
    /// The first shoko series id affected by this update.
    /// </summary>
    int? SeriesId => SeriesIds.Count > 0 ? SeriesIds[0] : null;

    /// <summary>
    /// Shoko series ids affected by this update.
    /// </summary>
    IReadOnlyList<int> SeriesIds { get; }

    /// <summary>
    /// The first shoko group id affected by this update.
    /// </summary>
    int? GroupId => GroupIds.Count > 0 ? GroupIds[0] : null;

    /// <summary>
    /// Shoko group ids affected by this update.
    /// </summary>
    IReadOnlyList<int> GroupIds { get; }
}