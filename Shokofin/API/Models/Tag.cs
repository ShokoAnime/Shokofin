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
    // All the abstract tags I know about.
    private static readonly HashSet<string> AbstractTags = new() {
        "/content indicators",
        "/dynamic",
        "/dynamic/cast",
        "/dynamic/ending",
        "/dynamic/storytelling",
        "/elements",
        "/elements/motifs",
        "/elements/pornography",
        "/elements/pornography/group sex",
        "/elements/pornography/oral",
        "/elements/sexual abuse",
        "/elements/speculative fiction",
        "/elements/tropes",
        "/fetishes",
        "/fetishes/breasts",
        "/maintenance tags",
        "/maintenance tags/TO BE MOVED TO CHARACTER",
        "/maintenance tags/TO BE MOVED TO EPISODE",
        "/origin",
        "/original work",
        "/setting",
        "/setting/place",
        "/setting/time",
        "/setting/time/season",
        "/target audience",
        "/technical aspects",
        "/technical aspects/adapted into other media",
        "/technical aspects/awards",
        "/technical aspects/multi-anime projects",
        "/themes",
        "/themes/body and host",
        "/themes/death",
        "/themes/family life",
        "/themes/money",
        "/themes/tales",
        "/ungrouped",
        "/unsorted",
        "/unsorted/character related tags which need deleting or merging",
        "/unsorted/ending tags that need merging",
        "/unsorted/old animetags",
    };

    private static readonly Dictionary<string, string> TagNameOverrides = new() {
        { "/fetishes/housewives", "MILF" },
        { "/setting/past", "Historical Past" },
        { "/setting/past/alternative past", "Alternative Past" },
        { "/setting/past/historical", "Historical Past" },
        { "/ungrouped/3dd cg", "3D CG animation" },
        { "/ungrouped/condom", "uses condom" },
        { "/ungrouped/dilf", "DILF" },
        { "/unsorted/old animetags/preview in ed", "preview in ED" },
        { "/unsorted/old animetags/recap in opening", "recap in OP" },
    };

    private static readonly Dictionary<string, string> TagNamespaceOverride = new() {
        { "/ungrouped/1950s", "/setting/time/past" },
        { "/ungrouped/1990s", "/setting/time/past" },
        { "/ungrouped/3dd cg", "/technical aspects/CGI" },
        { "/ungrouped/afterlife world", "/setting/place" },
        { "/ungrouped/airhead", "/maintenance tags/TO BE MOVED TO CHARACTER" },
        { "/ungrouped/airport", "/setting/place" },
        { "/ungrouped/anal prolapse", "/elements/pornography" },
        { "/ungrouped/child protagonist", "/dynamic/cast" },
        { "/ungrouped/condom", "/elements/pornography" },
        { "/ungrouped/dilf", "/fetishes" },
        { "/ungrouped/Italian-Japanese co-production", "/target audience" },
        { "/ungrouped/Middle-Aged Protagonist", "/dynamic/cast" },
        { "/ungrouped/creation magic", "/elements/speculative fiction/fantasy/magic" },
        { "/ungrouped/destruction magic", "/elements/speculative fiction/fantasy/magic" },
        { "/ungrouped/overpowered magic", "/elements/speculative fiction/fantasy/magic" },
        { "/ungrouped/paper talisman magic", "/elements/speculative fiction/fantasy/magic" },
        { "/ungrouped/space magic", "/elements/speculative fiction/fantasy/magic" },
        { "/ungrouped/very bloody wound in low-pg series", "/technical aspects" },
        { "/unsorted/ending tags that need merging/anti-climactic end", "/dynamic/ending" },
        { "/unsorted/ending tags that need merging/cliffhanger ending", "/dynamic/ending" },
        { "/unsorted/ending tags that need merging/complete manga adaptation", "/dynamic/ending" },
        { "/unsorted/ending tags that need merging/downer ending", "/dynamic/ending" },
        { "/unsorted/ending tags that need merging/incomplete story", "/dynamic/ending" },
        { "/unsorted/ending tags that need merging/only the beginning", "/dynamic/ending" },
        { "/unsorted/ending tags that need merging/series end", "/dynamic/ending" },
        { "/unsorted/ending tags that need merging/tragic ending", "/dynamic/ending" },
        { "/unsorted/ending tags that need merging/twisted ending", "/dynamic/ending" },
        { "/unsorted/ending tags that need merging/unresolved romance", "/dynamic/ending" },
        { "/unsorted/old animetags/preview in ed", "/technical aspects" },
        { "/unsorted/old animetags/recap in opening", "/technical aspects" },
    };

    private string? _displayName = null;

    public string DisplayName => _displayName ??= TagNameOverrides.TryGetValue(FullName, out var altName) ? altName : Name;

    private string? _fullName = null;

    public string FullName => _fullName ??= Namespace + Name;

    public bool IsParent => Children.Count is > 0;

    public bool IsAbstract => AbstractTags.Contains(FullName);

    public bool IsWeightless => !IsAbstract && Weight is 0;

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
        Namespace = TagNamespaceOverride.TryGetValue(ns + "/" + tag.Name, out var newNs) ? newNs : ns;
        Children = (getChildren(Source, Id) ?? Array.Empty<Tag>())
            .DistinctBy(childTag => childTag.Name)
            .Select(childTag => new ResolvedTag(childTag, this, getChildren, FullName + "/"))
            .ToDictionary(childTag => childTag.Name);
        RecursiveNamespacedChildren = Children.Values
            .SelectMany(childTag => childTag.RecursiveNamespacedChildren.Values.Prepend(childTag))
            .ToDictionary(childTag => childTag.FullName[FullName.Length..]);
    }
}