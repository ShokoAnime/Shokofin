#nullable enable
using System;
using System.Text.Json.Serialization;

#nullable enable
namespace Shokofin.API.Models;

public class Tag
{
    /// <summary>
    /// Tag id. Relative to it's source for now.
    /// </summary>
    [JsonPropertyName("ID")]
    public int Id { get; set; }

    /// <summary>
    /// Parent id relative to the source, if any.
    /// </summary>
    [JsonPropertyName("ParentID")]
    public int? ParentId { get; set; }

    /// <summary>
    /// The tag itself
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// What does the tag mean/what's it for
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// True if the tag has been verified.
    /// </summary>
    /// <remarks>
    /// For anidb does this mean the tag has been verified for use, and is not
    /// an unsorted tag. Also, anidb hides unverified tags from appearing in
    /// their UI except when the tags are edited.
    /// </remarks>
    public bool? IsVerified { get; set; }

    /// <summary>
    /// True if the tag is considered a spoiler for all series it appears on.
    /// </summary>
    public bool IsSpoiler { get; set; }

    /// <summary>
    /// True if the tag is considered a spoiler for that particular series it is
    /// set on.
    /// </summary>
    public bool? IsLocalSpoiler { get; set; }

    /// <summary>
    /// How relevant is it to the series
    /// </summary>
    public int? Weight { get; set; }

    /// <summary>
    /// When the tag info was last updated.
    /// </summary>
    public DateTime? LastUpdated { get; set; }

    /// <summary>
    /// Source. AniDB, User, etc.
    /// </summary>
    public string Source { get; set; } = string.Empty;
}
