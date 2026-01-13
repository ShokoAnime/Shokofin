using System.Text.Json.Serialization;
using Shokofin.API.Converters;

namespace Shokofin.API.Models;

public class ReleaseGroup {
    /// <summary>
    /// The AniDB Release Group ID (e.g. 1)
    /// /// </summary>
    [JsonPropertyName("ID"), JsonConverter(typeof(JsonAutoStringConverter))]
    public string? Id { get; set; }

    /// <summary>
    /// The release group's Name (e.g. "Unlimited Translation Works")
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The release group's Name (e.g. "UTW")
    /// </summary>
    public string? ShortName { get; set; }

    /// <summary>
    /// The release group's Source (e.g. "AniDB")
    /// </summary>
    public string? Source { get; set; }
}
