using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;

namespace Shokofin.Events.Interfaces;

public interface IMetadataUpdatedEventArgs {
    /// <summary>
    /// The update reason.
    /// </summary>
    UpdateReason Reason { get; }

    /// <summary>
    /// Indicates if this is an unknown update.
    /// </summary>
    bool IsUnknownUpdate => Reason is UpdateReason.None;

    /// <summary>
    /// Indicates if this is a metadata update.
    /// </summary>
    bool IsMetadataUpdate => Reason is UpdateReason.MetadataAdded or UpdateReason.MetadataUpdated or UpdateReason.MetadataRemoved;

    /// <summary>
    /// Indicates if this is an image update.
    /// </summary>
    bool IsImageUpdate => Reason is UpdateReason.ImageAdded or UpdateReason.ImageRemoved or UpdateReason.ImageUpdated;

    /// <summary>
    /// The provider metadata type.
    /// </summary>
    BaseItemKind Kind { get; }

    /// <summary>
    /// The provider metadata source.
    /// </summary>
    ProviderName ProviderName { get; }

    /// <summary>
    /// The provided metadata episode id.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Provider unique id.
    /// </summary>
    string ProviderUId => $"{ProviderName}:{ProviderId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// The provided metadata series id.
    /// </summary>
    int? ProviderParentId { get; }

    /// <summary>
    /// Provider unique parent id.
    /// </summary>
    string? ProviderParentUId => ProviderParentId.HasValue ? $"{ProviderName}:{ProviderParentId.Value.ToString(CultureInfo.InvariantCulture)}" : null;

    /// <summary>
    /// Shoko episode ids affected by this update.
    /// </summary>
    IReadOnlyList<int> EpisodeIds { get; }

    /// <summary>
    /// Shoko series ids affected by this update.
    /// </summary>
    IReadOnlyList<int> SeriesIds { get; }
}