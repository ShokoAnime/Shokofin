using System.Text.Json.Serialization;
using OrderType = Shokofin.Utils.Ordering.OrderType;
using PlaylistCreationType = Shokofin.Utils.Ordering.PlaylistCreationType;
using PlaylistTagFilter = Shokofin.Utils.Ordering.PlaylistTagFilter;
using SpecialOrderType = Shokofin.Utils.Ordering.SpecialOrderType;

namespace Shokofin.Configuration;

/// <summary>
/// Playlist reconstruction and generation configuration.
/// </summary>
public class PlaylistConfiguration {
    /// <summary>
    /// Automatically reconstruct playlists after a library scan.
    /// </summary>
    public bool AutoReconstruct { get; set; } = true;

    /// <summary>
    /// Determines how playlists are created.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PlaylistCreationType Grouping { get; set; } = PlaylistCreationType.None;

    /// <summary>
    /// Add a minimum requirement of two entries in a group before
    /// creating a playlist for it.
    /// </summary>
    public bool MinSizeOfTwo { get; set; } = true;

    /// <summary>
    /// Ordering to apply to the series/seasons within the playlist.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OrderType Ordering { get; set; } = OrderType.ReleaseDate;

    /// <summary>
    /// How to place specials in the playlist.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SpecialOrderType SpecialsPlacement { get; set; } = SpecialOrderType.InBetweenSeasonByAirDate;

    /// <summary>
    /// How to evaluate per-series playlist tags when deciding whether
    /// to create a playlist for a group.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PlaylistTagFilter TagFilter { get; set; } = PlaylistTagFilter.None;
}
