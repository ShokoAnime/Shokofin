using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

public class ComponentVersionSet
{
    /// <summary>
    /// Shoko.Server version.
    /// </summary>
    public ComponentVersion Server { get; set; } = new();
}

public class ComponentVersion
{
    /// <summary>
    /// Version number.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Commit SHA.
    /// </summary>
    public string? Commit { get; set; }

    /// <summary>
    /// Release channel.
    /// </summary>
    public ReleaseChannel? ReleaseChannel { get; set; }

    /// <summary>
    /// Release date.
    /// </summary>
    public DateTime? ReleaseDate { get; set; } = null;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleaseChannel
{
    Stable = 1,
    Dev = 2,
    Debug = 3,
}
