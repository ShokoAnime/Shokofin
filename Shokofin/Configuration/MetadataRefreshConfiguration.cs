using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Shokofin.Configuration;

/// <summary>
/// Metadata refresh configuration.
/// </summary>
public class MetadataRefreshConfiguration {
    /// <summary>
    /// Update metadata for all unaired episodes and movies.
    /// </summary>
    public bool UpdateUnaired { get; set; } = false;

    /// <summary>
    /// Number of days to look back when refreshing metadata.
    /// </summary>
    [Range(0, 365)]
    public int AutoRefreshRangeInDays { get; set; } = 7;

    /// <summary>
    /// Minimum number of hours to wait between two consecutive metadata
    /// refresh operations on the same entity.
    /// </summary>
    [Range(0, 8760 /* 24 * 365 */)]
    public int AntiRefreshDeadZoneInHours { get; set; } = 24;

    /// <summary>
    /// If above this range then we will always refresh the entity.
    /// </summary>
    [Range(0, 730 /* 365 * 2 */)]
    public int OutOfSyncInDays { get; set; } = 180;

    /// <summary>
    /// Fields to refresh for collections. Set to None to disable.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MetadataRefreshField Collection { get; set; } = MetadataRefreshField.LegacyRefresh;

    /// <summary>
    /// Fields to refresh for movies. Set to None to disable.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MetadataRefreshField Movie { get; set; } = MetadataRefreshField.LegacyRefresh;

    /// <summary>
    /// Fields to refresh for series. Set to None to disable.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MetadataRefreshField Series { get; set; } = MetadataRefreshField.LegacyRefresh;

    /// <summary>
    /// Fields to refresh for seasons. Set to None to disable.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MetadataRefreshField Season { get; set; } = MetadataRefreshField.LegacyRefresh;

    /// <summary>
    /// Fields to refresh for general videos (e.g. extras, trailers, etc.). Set to None to disable.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MetadataRefreshField Video { get; set; } = MetadataRefreshField.LegacyRefresh;

    /// <summary>
    /// Fields to refresh for episodes. Set to None to disable.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MetadataRefreshField Episode { get; set; } = MetadataRefreshField.LegacyRefresh;
}
