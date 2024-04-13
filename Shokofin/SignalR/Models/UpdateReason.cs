
using System.Text.Json.Serialization;

namespace Shokofin.SignalR.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UpdateReason
{
    None = 0,
    Added = 1,
    Updated = 2,
    Removed = 3,
}