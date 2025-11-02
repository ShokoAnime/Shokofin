
using System.Text.Json.Serialization;

namespace Shokofin.Events.Interfaces;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UpdateReason {
    /// <summary>
    /// No reason specified.
    /// </summary>
    None = 0,

    /// <summary>
    /// Metadata was added.
    /// </summary>
    [JsonStringEnumMemberName("Added")]
    MetadataAdded = 1,

    /// <summary>
    /// Metadata was updated.
    /// </summary>
    [JsonStringEnumMemberName("Updated")]
    MetadataUpdated = 2,

    /// <summary>
    /// Metadata was removed.
    /// </summary>
    [JsonStringEnumMemberName("Removed")]
    MetadataRemoved = 3,

    /// <summary>
    /// Images were added for the metadata.
    /// </summary>
    [JsonStringEnumMemberName("ImageAdded")]
    ImageAdded = 4,

    /// <summary>
    /// Images were updated for the metadata.
    /// </summary>
    [JsonStringEnumMemberName("ImageUpdated")]
    ImageUpdated = 5,

    /// <summary>
    /// Images were removed for the metadata.
    /// </summary>
    [JsonStringEnumMemberName("ImageRemoved")]
    ImageRemoved = 6,
}
