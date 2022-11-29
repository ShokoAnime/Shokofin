#nullable enable
using System.Text.Json.Serialization;

#nullable enable
namespace Shokofin.API.Models;

public class Tag
{
    /// <summary>
    /// Tag id. Relative to it's source for now.
    /// </summary>
    /// <value></value>
    [JsonPropertyName("ID")]
    public int Id { get; set; }

    /// <summary>
    /// The tag itself
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// What does the tag mean/what's it for
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// How relevant is it to the series
    /// </summary>
    public int? Weight { get; set; }

    /// <summary>
    /// Source. AniDB, User, etc.
    /// </summary>
    /// <value></value>
    public string Source { get; set; } = "";
}
