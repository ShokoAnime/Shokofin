
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Shokofin.API.Info;
using Shokofin.API.Models;
using Shokofin.Events.Interfaces;

namespace Shokofin.Utils;

public static class TagFilter
{
    /// <summary>
    /// Include only the children of the selected tags.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
    public class TagSourceIncludeAttribute : Attribute
    {
        public string[] Values { get; init; }

        public TagSourceIncludeAttribute(params string[] values)
        {
            Values = values;
        }
    }

    /// <summary>
    /// Include only the selected tags, but not their children.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
    public class TagSourceIncludeOnlyAttribute : Attribute
    {
        public string[] Values { get; init; }

        public TagSourceIncludeOnlyAttribute(params string[] values)
        {
            Values = values;
        }
    }

    /// <summary>
    /// Exclude the selected tags and all their children.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
    public class TagSourceExcludeOnlyAttribute : Attribute
    {
        public string[] Values { get; init; }
        
        public TagSourceExcludeOnlyAttribute(params string[] values)
        {
            Values = values;
        }
    }

    /// <summary>
    /// Exclude the selected tags, but don't exclude their children.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
    public class TagSourceExcludeAttribute : Attribute
    {
        public string[] Values { get; init; }
        
        public TagSourceExcludeAttribute(params string[] values)
        {
            Values = values;
        }
    }

    /// <summary>
    /// All available tag sources to use.
    /// </summary>
    [Flags]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TagSource {
        /// <summary>
        /// The content indicators branch is intended to be a less geographically specific
        /// tool than the `age rating` used by convention, for warning about things that
        /// might cause offense. Obviously there is still a degree of subjectivity
        /// involved, but hopefully it will prove useful for parents with delicate
        /// children, or children with delicate parents.
        /// </summary>
        [TagSourceInclude("/content indicators")]
        ContentIndicators = 1 << 0,

        /// <summary>
        /// Central structural elements in the anime.
        /// </summary>
        [TagSourceInclude("/dynamic")]
        [TagSourceExclude("/dynamic/cast", "/dynamic/ending")]
        Dynamic = 1 << 1,

        /// <summary>
        /// Cast related structural elements in the anime.
        /// </summary>
        [TagSourceInclude("/dynamic/cast")]
        DynamicCast = 1 << 2,

        /// <summary>
        /// Ending related structural elements in the anime.
        /// </summary>
        [TagSourceInclude("/dynamic/ending")]
        DynamicEnding = 1 << 3,

        // 4 is reserved for story telling if we add it as a separate source.

        /// <summary>
        /// Next to <see cref="Themes"/> setting the backdrop for the protagonists in the
        /// anime, there are the more detailed plot elements that centre on character
        /// interactions: "What do characters do to each other or what is done to them?".
        /// Is it violent action, an awe-inspiring adventure in a foreign place, the
        /// gripping life of a detective, a slapstick comedy, an ecchi harem anime,
        /// a sci-fi epic, or some fantasy traveling adventure, etc..
        /// </summary>
        [TagSourceInclude("/elements/speculative fiction", "/elements")]
        [TagSourceExclude("/elements/pornography", "/elements/sexual abuse", "/elements/tropes", "/elements/motifs")]
        [TagSourceExcludeOnly("/elements/speculative fiction")]
        Elements = 1 << 5,

        /// <summary>
        /// Anime clearly marked as "Restricted 18" material centring on all variations of
        /// adult sex, some of which can be considered as quite perverse. To a certain
        /// extent, some of the elements can be seen on late night TV animations. Sexual
        /// abuse is the act of one person forcing sexual activities upon another. Sexual
        /// abuse includes not only physical coercion and sexual assault, especially rape,
        /// but also psychological abuse, such as verbal sexual behavior or stalking,
        /// including emotional manipulation.
        /// </summary>
        [TagSourceInclude("/elements/pornography", "/elements/sexual abuse")]
        ElementsPornographyAndSexualAbuse = 1 << 6,

        /// <summary>
        /// A trope is a commonly recurring literary and rhetorical devices, motifs or
        /// clichés in creative works.
        /// </summary>
        [TagSourceInclude("/elements/tropes", "/elements/motifs")]
        ElementsTropesAndMotifs = 1 << 7,

        /// <summary>
        /// For non-porn anime, the fetish must be a major element of the show; incidental
        /// appearances of the fetish is not sufficient for a fetish tag. Please do not
        /// add fetish tags to anime that do not pander to the fetish in question in any
        /// meaningful way. For example, there's some ecchi in Shinseiki Evangelion, but
        /// the fact you get to see Asuka's panties is not sufficient to warrant applying
        /// the school girl fetish tag. Most porn anime play out the fetish, making tag
        /// application fairly straightforward.
        /// </summary>
        [TagSourceInclude("/fetishes/breasts", "/fetishes")]
        [TagSourceExcludeOnly("/fetishes/breasts")]
        Fetishes = 1 << 8,

        /// <summary>
        /// Origin production locations.
        /// </summary>
        [TagSourceInclude("/origin")]
        [TagSourceExcludeOnly("/origin/development hell", "/origin/fan-made", "/origin/remake")]
        OriginProduction = 1 << 9,

        /// <summary>
        /// Origin development information.
        /// </summary>
        [TagSourceIncludeOnly("/origin/development hell", "/origin/fan-made", "/origin/remake")]
        OriginDevelopment = 1 << 10,

        /// <summary>
        /// The places the anime takes place in. Includes more specific places such as a
        /// country on Earth, as well as more general places such as a dystopia or a
        /// mirror world.
        /// </summary>
        [TagSourceInclude("/setting/place")]
        [TagSourceExcludeOnly("/settings/place/Earth")]
        SettingPlace = 1 << 11,

        /// <summary>
        /// This placeholder lists different epochs in human history and more vague but
        /// important timelines such as the future, the present and the past.
        /// </summary>
        [TagSourceInclude("/setting/time")]
        [TagSourceExclude("/setting/time/season")]
        SettingTimePeriod = 1 << 12,

        /// <summary>
        /// In temperate and sub-polar regions, four calendar-based seasons (with their
        /// adjectives) are generally recognized:
        /// - spring (vernal),
        /// - summer (estival),
        /// - autumn/fall (autumnal), and
        /// - winter (hibernal).
        /// </summary>
        [TagSourceInclude("/setting/time/season")]
        SettingTimeSeason = 1 << 13,

        /// <summary>
        /// What the anime is based on! This is given as the original work credit in the
        /// OP. Mostly of academic interest, but a useful bit of info, hinting at the
        /// possible depth of story.
        /// </summary>
        /// <remarks>
        /// This is not sourced from the tags, but rather from the dedicated method.
        /// </remarks>
        SourceMaterial = 1 << 14,

        /// <summary>
        /// Anime, like everything else in the modern world, is targeted towards specific
        /// audiences, both implicitly by the creators and overtly by the marketing.
        /// </summary>
        [TagSourceInclude("/target audience")]
        TargetAudience = 1 << 15,

        /// <summary>
        /// It may sometimes be useful to know about technical aspects of a show, such as
        /// information about its broadcasting or censorship. Such information can be
        /// found here.
        /// </summary>
        [TagSourceInclude("/technical aspects")]
        [TagSourceExclude("/technical aspects/adapted into other media", "/technical aspects/awards", "/technical aspects/multi-anime projects")]
        TechnicalAspects = 1 << 16,

        /// <summary>
        /// This anime is a new original work, and it has been adapted into other media
        /// formats.
        ///
        /// In exceedingly rare instances, a specific episode of a new original work anime
        /// can also be adapted.
        /// </summary>
        [TagSourceInclude("/technical aspects/adapted into other media")]
        TechnicalAspectsAdaptions = 1 << 17,

        /// <summary>
        /// Awards won by the anime.
        /// </summary>
        [TagSourceInclude("/technical aspects/awards")]
        TechnicalAspectsAwards = 1 << 18,

        /// <summary>
        /// Many anime are created as part of larger projects encompassing many shows
        /// without direct  relation to one another. Normally, there is a specific idea in
        /// mind: for example, the Young Animator Training Project aims to stimulate the
        /// on-the-job training of next-generation professionals of the anime industry,
        /// whereas the World Masterpiece Theatre aims to animate classical stories from
        /// all over the world.
        /// </summary>
        [TagSourceInclude("/technical aspects/multi-anime projects")]
        TechnicalAspectsMultiAnimeProjects = 1 << 19,

        /// <summary>
        /// Themes describe the very central elements important to the anime stories. They
        /// set the backdrop against which the protagonists must face their challenges.
        /// Be it school life, present daily life, military action, cyberpunk, law and
        /// order detective work, sports, or the underworld. These are only but a few of
        /// the more typical backgrounds for anime plots. Add to that a conspiracy setting
        /// with a possible tragic outcome, the themes span most of the imaginable subject
        /// matter relevant to the anime.
        /// </summary>
        [TagSourceInclude("/themes")]
        [TagSourceExclude("/themes/death", "/themes/tales")]
        [TagSourceExcludeOnly("/themes/body and host", "/themes/family life", "/themes/money")]
        Themes = 1 << 20,

        // 21 to 23 are reserved for the above exclusions if we decide to branch them off
        // into their own source.

        /// <summary>
        /// Death is the state of no longer being alive or the process of ceasing to be
        /// alive. As Emiya Shirou once said it; "People die when they're killed."
        /// </summary>
        [TagSourceInclude("/themes/death")]
        ThemesDeath = 1 << 24,

        /// <summary>
        /// Tales are stories told time and again and passed down from generation to
        /// generation, and some of those show up in anime not just once or twice, but
        /// several times.
        /// </summary>
        [TagSourceInclude("/themes/tales")]
        ThemesTales = 1 << 25,

        /// <summary>
        /// Everything under the ungrouped tag.
        /// </summary>
        [TagSourceInclude("/ungrouped")]
        Ungrouped = 1 << 26,

        /// <summary>
        /// Everything under the unsorted tag.
        /// </summary>
        [TagSourceInclude("/unsorted")]
        [TagSourceExclude("/unsorted/old animetags", "/unsorted/ending tags that need merging", "/unsorted/character related tags which need deleting or merging")]
        Unsorted = 1 << 27,

        /// <summary>
        /// Custom user tags.
        /// </summary>
        [TagSourceInclude("/custom user tags")]
        CustomTags = 1 << 30,
    }

    [Flags]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TagIncludeFilter {
        Parent = 1,
        Child = 2,
        Abstract = 4,
        Weightless = 8,
        Weighted = 16,
        GlobalSpoiler = 32,
        LocalSpoiler = 64,
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TagWeight {
        Weightless = 0,
        One = 100,
        Two = 200,
        Three = 300,
        Four = 400,
        Five = 500,
        Six = 600,
    }

    private static ProviderName[] GetOrderedProductionLocationProviders()
        => Plugin.Instance.Configuration.ProductionLocationOverride
            ? Plugin.Instance.Configuration.ProductionLocationOrder.Where((t) => Plugin.Instance.Configuration.ProductionLocationList.Contains(t)).ToArray()
            : [ProviderName.AniDB, ProviderName.TMDB];

#pragma warning disable IDE0060
    public static IReadOnlyList<string> GetMovieContentRating(SeasonInfo seasonInfo, EpisodeInfo episodeInfo)
#pragma warning restore IDE0060
    {
        // TODO: Add TMDB movie linked to episode content rating here.
        foreach (var provider in GetOrderedProductionLocationProviders()) {
            var title = provider switch {
                ProviderName.AniDB => seasonInfo.ProductionLocations,
                // TODO: Add TMDB series content rating here.
                _ => [],
            };
            if (title.Count > 0)
                return title;
        }
        return [];
    }

    public static IReadOnlyList<string> GetSeasonContentRating(SeasonInfo seasonInfo)
    {
        foreach (var provider in GetOrderedProductionLocationProviders()) {
            var title = provider switch {
                ProviderName.AniDB => seasonInfo.ProductionLocations,
                // TODO: Add TMDB series content rating here.
                _ => [],
            };
            if (title.Count > 0)
                return title;
        }
        return [];
    }

    public static IReadOnlyList<string> GetShowContentRating(ShowInfo showInfo)
    {
        foreach (var provider in GetOrderedProductionLocationProviders()) {
            var title = provider switch {
                ProviderName.AniDB => showInfo.ProductionLocations,
                // TODO: Add TMDB series content rating here.
                _ => [],
            };
            if (title.Count > 0)
                return title;
        }
        return [];
    }

    public static string[] FilterTags(IReadOnlyDictionary<string, ResolvedTag> tags)
    {
        var config = Plugin.Instance.Configuration;
        if (!config.TagOverride)
            return FilterInternal(
                tags,
                TagSource.ContentIndicators | TagSource.Dynamic | TagSource.DynamicCast | TagSource.DynamicEnding | TagSource.Elements |
                TagSource.ElementsPornographyAndSexualAbuse | TagSource.ElementsTropesAndMotifs | TagSource.Fetishes |
                TagSource.OriginProduction | TagSource.OriginDevelopment | TagSource.SourceMaterial | TagSource.SettingPlace |
                TagSource.SettingTimePeriod | TagSource.SettingTimeSeason | TagSource.TargetAudience | TagSource.TechnicalAspects |
                TagSource.TechnicalAspectsAdaptions | TagSource.TechnicalAspectsAwards | TagSource.TechnicalAspectsMultiAnimeProjects |
                TagSource.Themes | TagSource.ThemesDeath | TagSource.ThemesTales | TagSource.CustomTags,
                TagIncludeFilter.Parent | TagIncludeFilter.Child | TagIncludeFilter.Abstract | TagIncludeFilter.Weightless | TagIncludeFilter.Weighted,
                TagWeight.Weightless,
                0
            );
        return FilterInternal(tags, config.TagSources, config.TagIncludeFilters, config.TagMinimumWeight, config.TagMaximumDepth);
    }

    public static string[] FilterGenres(IReadOnlyDictionary<string, ResolvedTag> tags)
    {
        var config = Plugin.Instance.Configuration;
        if (!config.GenreOverride)
            return FilterInternal(
                tags,
                TagSource.SourceMaterial | TagSource.TargetAudience | TagSource.Elements,
                TagIncludeFilter.Parent | TagIncludeFilter.Child | TagIncludeFilter.Abstract | TagIncludeFilter.Weightless | TagIncludeFilter.Weighted,
                TagWeight.Four,
                1
            );
        return FilterInternal(tags, config.GenreSources, config.GenreIncludeFilters, config.GenreMinimumWeight, config.GenreMaximumDepth);
    }

    private static readonly HashSet<TagSource> AllFlagsToUse = Enum.GetValues<TagSource>().Except([TagSource.CustomTags]).ToHashSet();

    private static readonly HashSet<TagSource> AllFlagsToUseForCustomTags = AllFlagsToUse.Except([TagSource.SourceMaterial, TagSource.TargetAudience]).ToHashSet();

    private static string[] FilterInternal(IReadOnlyDictionary<string, ResolvedTag> tags, TagSource source, TagIncludeFilter includeFilter, TagWeight minWeight = TagWeight.Weightless, int maxDepth = 0)
    {
        var tagSet = new List<string>();
        foreach (var flag in AllFlagsToUse.Where(flag => source.HasFlag(flag)))
            tagSet.AddRange(GetTagsFromSource(tags, flag, includeFilter, minWeight, maxDepth));

        if (source.HasFlag(TagSource.CustomTags) && tags.TryGetValue("/custom user tags", out var customTags)) {
            var count = tagSet.Count;
            tagSet.AddRange(customTags.Children.Values.Where(tag => !tag.IsParent).Select(SelectTagName));
            count = tagSet.Count - count;

            // If we have any children that weren't added above, then run the additional checks on them.
            if (customTags.RecursiveNamespacedChildren.Count != count)
                foreach (var flag in AllFlagsToUseForCustomTags.Where(flag => source.HasFlag(flag)))
                    tagSet.AddRange(GetTagsFromSource(customTags.RecursiveNamespacedChildren, flag, includeFilter, minWeight, maxDepth));
        }

        return tagSet
            .Distinct()
            .OrderBy(a => a)
            .ToArray();
    }

    private static HashSet<string> GetTagsFromSource(IReadOnlyDictionary<string, ResolvedTag> tags, TagSource source, TagIncludeFilter includeFilter, TagWeight minWeight, int maxDepth)
    {
        if (source is TagSource.SourceMaterial)
            return [GetSourceMaterial(tags)];

        var tagSet = new HashSet<string>();
        var exceptTags = new List<ResolvedTag>();
        var includeTags = new List<KeyValuePair<string, ResolvedTag>>();
        var field = source.GetType().GetField(source.ToString())!;
        var includeAttributes = field.GetCustomAttributes<TagSourceIncludeAttribute>();
        if (includeAttributes.Length is 1)
            foreach (var tagName in includeAttributes.First().Values)
                if (tags.TryGetValue(tagName, out var tag))
                    includeTags.AddRange(tag.RecursiveNamespacedChildren);

        var includeOnlyAttributes = field.GetCustomAttributes<TagSourceIncludeOnlyAttribute>();
        if (includeOnlyAttributes.Length is 1)
            foreach (var tagName in includeOnlyAttributes.First().Values)
                if (tags.TryGetValue(tagName, out var tag))
                    includeTags.Add(KeyValuePair.Create($"/{tag.Name}", tag));

        var excludeAttributes = field.GetCustomAttributes<TagSourceExcludeAttribute>();
        if (excludeAttributes.Length is 1)
            foreach (var tagName in excludeAttributes.First().Values)
                if (tags.TryGetValue(tagName, out var tag))
                    exceptTags.AddRange(tag.RecursiveNamespacedChildren.Values.Append(tag));

        var excludeOnlyAttributes = field.GetCustomAttributes<TagSourceExcludeOnlyAttribute>();
        if (excludeOnlyAttributes.Length is 1)
            foreach (var tagName in excludeOnlyAttributes.First().Values)
                if (tags.TryGetValue(tagName, out var tag))
                    exceptTags.Add(tag);

        includeTags = includeTags
            .DistinctBy(pair => $"{pair.Value.Source}:{pair.Value.Id}")
            .ExceptBy(exceptTags, pair => pair.Value)
            .ToList();
        foreach (var (relativeName, tag) in includeTags) {
            var depth = relativeName[1..].Split('/').Length;
            if (maxDepth > 0 && depth > maxDepth)
                continue;
            if (tag.IsLocalSpoiler && !includeFilter.HasFlag(TagIncludeFilter.LocalSpoiler))
                continue;
            if (tag.IsGlobalSpoiler && !includeFilter.HasFlag(TagIncludeFilter.GlobalSpoiler))
                continue;
            if (tag.IsAbstract && !includeFilter.HasFlag(TagIncludeFilter.Abstract))
                continue;
            if (tag.IsWeightless ? !includeFilter.HasFlag(TagIncludeFilter.Weightless) : !includeFilter.HasFlag(TagIncludeFilter.Weighted))
                continue;
            if (tag.IsParent ? !includeFilter.HasFlag(TagIncludeFilter.Parent) : !includeFilter.HasFlag(TagIncludeFilter.Child))
                continue;
            if (minWeight is > TagWeight.Weightless && !tag.IsWeightless && tag.Weight < minWeight)
                continue;
            tagSet.Add(SelectTagName(tag));
        }

        return tagSet;
    }

    private static string GetSourceMaterial(IReadOnlyDictionary<string, ResolvedTag> tags)
    {
        if (!tags.TryGetValue("/source material", out var sourceMaterial) || sourceMaterial.Children.ContainsKey("Original Work"))
            return "Original Work";

        var firstSource = sourceMaterial.Children.Keys.OrderBy(material => material).FirstOrDefault()?.ToLowerInvariant();
        return firstSource switch {
            "american derived" => "Adapted From Western Media",
            "manga" => "Adapted From A Manga",
            "manhua" => "Adapted From A Manhua",
            "manhwa" => "Adapted from a Manhwa",
            "movie" => "Adapted From A Live-Action Movie",
            "novel" => "Adapted From A Novel",
            "game" => sourceMaterial.Children[firstSource]!.Children.Keys.OrderBy(material => material).FirstOrDefault()?.ToLowerInvariant() switch {
                "erotic game" => "Adapted From An Eroge",
                "visual novel" => "Adapted From A Visual Novel",
                _ => "Adapted From A Video Game",
            },
            "television programme" => sourceMaterial.Children[firstSource]!.Children.Keys.OrderBy(material => material).FirstOrDefault()?.ToLowerInvariant() switch {
                "korean drama" => "Adapted From A Korean Drama",
                _ => "Adapted From A Live-Action Show",
            },
            "radio programme" => "Radio Programme",
            "western animated cartoon" => "Adapted From Western Media",
            "western comics" => "Adapted From Western Media",
            _ => "Original Work",
        };
    }

    public static string[] GetProductionCountriesFromTags(IReadOnlyDictionary<string, ResolvedTag> tags)
    {
        if (!tags.TryGetValue("/origin", out var origin))
            return [];

        var productionCountries = new List<string>();
        foreach (var childTag in origin.Children.Keys) {
            productionCountries.AddRange(childTag.ToLowerInvariant() switch {
                "american-japanese co-production" => new string[] { "Japan", "United States of America" },
                "chinese production" => ["China"],
                "french-chinese co-production" => ["France", "China"],
                "french-japanese co-production" => ["Japan", "France"],
                "indo-japanese co-production" => ["Japan", "India"],
                "japanese production" => ["Japan"],
                "korean-japanese co-production" => ["Japan", "Republic of Korea"],
                "north korean production" => ["Democratic People's Republic of Korea"],
                "polish-japanese co-production" => ["Japan", "Poland"],
                "russian-japanese co-production" => ["Japan", "Russia"],
                "saudi arabian-japanese co-production" => ["Japan", "Saudi Arabia"],
                "italian-japanese co-production" => ["Japan", "Italy"],
                "singaporean production" => ["Singapore"],
                "sino-japanese co-production" => ["Japan", "China"],
                "south korea production" => ["Republic of Korea"],
                "taiwanese production" => ["Taiwan"],
                "thai production" => ["Thailand"],
                _ => [],
            });
        }
        return productionCountries
            .Distinct()
            .ToArray();
    }

    private static string SelectTagName(ResolvedTag tag)
        => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(tag.DisplayName);

    private static bool HasAnyFlags(this Enum value, params Enum[] candidates)
        => candidates.Any(value.HasFlag);
}