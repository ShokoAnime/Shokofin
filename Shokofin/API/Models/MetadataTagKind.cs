using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

/// <summary>
/// The kinds of metadata tags.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MetadataTagKind {
    Unknown = 0,
    Tag = 1,
    Genre = 2,
    Keyword = 3,
}
