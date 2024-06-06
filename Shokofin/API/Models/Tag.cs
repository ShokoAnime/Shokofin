using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

using TagWeight = Shokofin.Utils.TagFilter.TagWeight;

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
    [JsonPropertyName("IsSpoiler")]
    public bool IsGlobalSpoiler { get; set; }

    /// <summary>
    /// True if the tag is considered a spoiler for that particular series it is
    /// set on.
    /// </summary>
    public bool? IsLocalSpoiler { get; set; }

    /// <summary>
    /// How relevant is it to the series
    /// </summary>
    public TagWeight? Weight { get; set; }

    /// <summary>
    /// When the tag info was last updated.
    /// </summary>
    public DateTime? LastUpdated { get; set; }

    /// <summary>
    /// Source. AniDB, User, etc.
    /// </summary>
    public string Source { get; set; } = string.Empty;
}

public class ResolvedTag : Tag
{
    private string? _fullName = null;
    
    public string FullName => _fullName ??= Namespace + Name;

    public bool IsParent => Children.Count is > 0;

    public bool IsWeightless => Children.Count is 0 && Weight is 0;

    /// <summary>
    /// True if the tag is considered a spoiler for that particular series it is
    /// set on.
    /// </summary>
    public new bool IsLocalSpoiler;

    /// <summary>
    /// How relevant is it to the series
    /// </summary>
    public new TagWeight Weight;

    public string Namespace;

    public ResolvedTag? Parent;

    public IReadOnlyDictionary<string, ResolvedTag> Children;

    public IReadOnlyDictionary<string, ResolvedTag> RecursiveNamespacedChildren;

    public ResolvedTag(Tag tag, ResolvedTag? parent, Func<string, int, IEnumerable<Tag>?> getChildren, string ns = "/")
    {
        Id = tag.Id;
        ParentId = parent?.Id;
        Name = tag.Name;
        Description = tag.Description;
        IsVerified = tag.IsVerified;
        IsGlobalSpoiler = tag.IsGlobalSpoiler || (parent?.IsGlobalSpoiler ?? false);
        IsLocalSpoiler = tag.IsLocalSpoiler ?? parent?.IsLocalSpoiler ?? false;
        Weight = tag.Weight ?? TagWeight.Weightless;
        LastUpdated = tag.LastUpdated;
        Source = tag.Source;
        Namespace = ns;
        Parent = parent;
        Children = (getChildren(Source, Id) ?? Array.Empty<Tag>())
            .DistinctBy(childTag => childTag.Name)
            .Select(childTag => new ResolvedTag(childTag, this, getChildren, ns + tag.Name + "/"))
            .ToDictionary(childTag => childTag.Name);
        RecursiveNamespacedChildren = Children.Values
            .SelectMany(childTag => childTag.RecursiveNamespacedChildren.Values.Prepend(childTag))
            .ToDictionary(childTag => childTag.FullName[(ns.Length + Name.Length)..]);
    }
}