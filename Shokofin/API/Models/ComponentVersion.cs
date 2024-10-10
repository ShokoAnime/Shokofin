using System;
using System.ComponentModel;
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
    [DefaultValue("1.0.0.0")]
    public Version Version { get; set; } = new("1.0.0.0");

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

    public override string ToString()
    {
        var extraDetails = new string?[3] {
            ReleaseChannel?.ToString(),
            Commit?[0..7],
            ReleaseDate?.ToUniversalTime().ToString("yyyy-MM-ddThh:mm:ssZ"),
        }.Where(s => !string.IsNullOrEmpty(s)).OfType<string>().Join(", ");
        if (extraDetails.Length == 0)
            return $"Version {Version}";

        return $"Version {Version} ({extraDetails})";
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleaseChannel
{
    Stable = 1,
    Dev = 2,
    Debug = 3,
}
