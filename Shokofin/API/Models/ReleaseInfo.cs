using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

public class ReleaseInfo {
    /// <summary>
    /// Blu-ray, DVD, LD, TV, etc..
    /// </summary>
    [JsonInclude, JsonConverter(typeof(JsonStringEnumConverter))]
    public ReleaseSource Source { get; set; }

    /// <summary>
    /// The Release Group.
    /// </summary>
    [JsonInclude, JsonPropertyName("Group")]
    public ReleaseGroup? Group { get; set; }

    /// <summary>
    /// The Release Group.
    /// </summary>
    [JsonInclude, JsonPropertyName("ReleaseGroup")]
    public ReleaseGroup? LegacyGroup {
        get => Group;
        set => Group = value;
    }

    /// <summary>
    /// The file's version.
    /// </summary>
    public int Version { get; set; }
}
