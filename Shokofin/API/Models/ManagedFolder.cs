using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

public class ManagedFolder {
    /// <summary>
    /// The ID of the managed folder.
    /// </summary>
    [JsonPropertyName("ID")]
    public int Id { get; set; }

    /// <summary>
    /// The friendly name of the managed folder, if any.
    /// </summary>
    public string? Name { get; set; }
}