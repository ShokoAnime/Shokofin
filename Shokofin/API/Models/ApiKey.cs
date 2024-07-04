
using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

public class ApiKey
{
    /// <summary>
    /// The Api Key Token.
    /// </summary>
    [JsonPropertyName("apikey")]
    public string Token { get; set; } = string.Empty;
}
