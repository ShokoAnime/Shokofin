
using System.Text.Json.Serialization;

namespace Shokofin.Events.Interfaces;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UpdateReason {
    None = 0,
    Added = 1,
    Updated = 2,
    Removed = 3,
    ImageAdded = 4,
    ImageUpdated = 5,
    ImageRemoved = 6,
}