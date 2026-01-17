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
    MetadataAdded = 1,

    /// <summary>
    /// Alias for <see cref="MetadataAdded"/>.
    /// </summary>
    Added = MetadataAdded,

    /// <summary>
    /// Metadata was updated.
    /// </summary>
    MetadataUpdated = 2,

    /// <summary>
    /// Alias for <see cref="MetadataUpdated"/>.
    /// </summary>
    Updated = MetadataUpdated,

    /// <summary>
    /// Metadata was removed.
    /// </summary>
    MetadataRemoved = 3,

    /// <summary>
    /// Alias for <see cref="MetadataRemoved"/>.
    /// </summary>
    Removed = MetadataRemoved,

    /// <summary>
    /// Images were added for the metadata.
    /// </summary>
    ImageAdded = 4,

    /// <summary>
    /// Images were updated for the metadata.
    /// </summary>
    ImageUpdated = 5,

    /// <summary>
    /// Images were removed for the metadata.
    /// </summary>
    ImageRemoved = 6,
}
