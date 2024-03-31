using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

public class IDs
{
    [JsonPropertyName("ID")]
    public int Shoko { get; set; }
}
