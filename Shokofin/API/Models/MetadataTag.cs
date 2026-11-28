namespace Shokofin.API.Models;

/// <summary>
/// A tag, genre or keyword provided by a metadata source plugin on Shoko Server
/// as returned by the <c>/api/v3/Metadata/{source}/Series/{id}/Tags</c> endpoint.
/// </summary>
public class MetadataTag {
    /// <summary>
    /// The source of the tag, e.g. "AniList".
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// The display name of the tag.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The kind of tag.
    /// </summary>
    public MetadataTagKind Kind { get; set; }

    /// <summary>
    /// True if the tag is considered a spoiler for all series it appears on.
    /// </summary>
    public bool? IsSpoiler { get; set; }

    /// <summary>
    /// True if the tag is considered a spoiler for the series it is set on.
    /// </summary>
    public bool? IsLocalSpoiler { get; set; }

    /// <summary>
    /// True if the tag is restricted to adult content.
    /// </summary>
    public bool? IsRestricted { get; set; }

    /// <summary>
    /// How relevant the tag is for the series, if provided by the source. The
    /// scale is defined by the source.
    /// </summary>
    public double? Weight { get; set; }
}
