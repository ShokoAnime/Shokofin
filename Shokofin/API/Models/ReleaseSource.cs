using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleaseSource {
    Unknown = 0,
    Other = 1,
    TV = 2,
    DVD = 3,
    BluRay = 4,
    Web = 5,
    VHS = 6,
    VCD = 7,
    LaserDisc = 8,
    Camera = 9
}
