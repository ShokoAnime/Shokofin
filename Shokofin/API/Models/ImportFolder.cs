using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

public class ImportFolder
{
    /// <summary>
    /// The ID of the import folder.
    /// </summary>
    [JsonPropertyName("ID")]
    public int Id { get; set; }

    /// <summary>
    /// The friendly name of the import folder, if any.
    /// </summary>
    public string? Name { get; set; }
}