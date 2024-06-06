
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
    [Flags]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TagSource {
        SourceMaterial = 1,
        TargetAudience = 2,
        ContentIndicators = 4,
        Origin = 8,
        Elements = 16,
        Themes = 32,
        Fetishes = 64,
        SettingPlace = 128,
        SettingTimePeriod = 256,
        SettingTimeSeason = 512,
        CustomTags = 1024,
    }

    [Flags]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TagIncludeFilter {
        Parent = 1,
        Child = 2,
        Weightless = 4,
        GlobalSpoiler = 8,
        LocalSpoiler = 16,
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
            : new ProviderName[] { ProviderName.AniDB, ProviderName.TMDB };

#pragma warning disable IDE0060
    public static IReadOnlyList<string> GetMovieContentRating(SeasonInfo seasonInfo, EpisodeInfo episodeInfo)
#pragma warning restore IDE0060
    {
        // TODO: Add TMDB movie linked to episode content rating here.
        foreach (var provider in GetOrderedProductionLocationProviders()) {
            var title = provider switch {
                ProviderName.AniDB => seasonInfo.ProductionLocations,
                // TODO: Add TMDB series content rating here.
                _ => Array.Empty<string>(),
            };
            if (title.Count > 0)
                return title;
        }
        return Array.Empty<string>();
    }

    public static IReadOnlyList<string> GetSeasonContentRating(SeasonInfo seasonInfo)
    {
        foreach (var provider in GetOrderedProductionLocationProviders()) {
            var title = provider switch {
                ProviderName.AniDB => seasonInfo.ProductionLocations,
                // TODO: Add TMDB series content rating here.
                _ => Array.Empty<string>(),
            };
            if (title.Count > 0)
                return title;
        }
        return Array.Empty<string>();
    }

    public static IReadOnlyList<string> GetShowContentRating(ShowInfo showInfo)
    {
        foreach (var provider in GetOrderedProductionLocationProviders()) {
            var title = provider switch {
                ProviderName.AniDB => showInfo.ProductionLocations,
                // TODO: Add TMDB series content rating here.
                _ => Array.Empty<string>(),
            };
            if (title.Count > 0)
                return title;
        }
        return Array.Empty<string>();
    }

    public static string[] FilterTags(IReadOnlyDictionary<string, ResolvedTag> tags)
    {
        var config = Plugin.Instance.Configuration;
        if (!config.TagOverride)
            return FilterInternal(
                tags,
                TagSource.Elements | TagSource.Themes | TagSource.Fetishes | TagSource.SettingPlace | TagSource.SettingTimePeriod | TagSource.SettingTimeSeason | TagSource.CustomTags,
                TagIncludeFilter.Parent | TagIncludeFilter.Child | TagIncludeFilter.Weightless,
                TagWeight.Weightless
            );
        return FilterInternal(tags, config.TagSources, config.TagIncludeFilters, config.TagMinimumWeight);
    }

    public static string[] FilterGenres(IReadOnlyDictionary<string, ResolvedTag> tags)
    {
        var config = Plugin.Instance.Configuration;
        if (!config.GenreOverride)
            return FilterInternal(
                tags,
                TagSource.SourceMaterial | TagSource.TargetAudience | TagSource.ContentIndicators | TagSource.Elements,
                TagIncludeFilter.Child | TagIncludeFilter.Weightless,
                TagWeight.Three
            );
        return FilterInternal(tags, config.GenreSources, config.GenreIncludeFilters, config.GenreMinimumWeight);
    }

    private static string[] FilterInternal(IReadOnlyDictionary<string, ResolvedTag> tags, TagSource source, TagIncludeFilter includeFilter, TagWeight minWeight = TagWeight.Weightless)
    {
        var tagSet = new List<string>();
        if (source.HasFlag(TagSource.SourceMaterial))
            tagSet.Add(GetSourceMaterial(tags));
        if (source.HasFlag(TagSource.TargetAudience) && tags.TryGetValue("/target audience", out var subTags))
            foreach (var tag in subTags.Children.Values.Select(SelectTagName))
                tagSet.Add(tag);
        if (source.HasFlag(TagSource.ContentIndicators) && tags.TryGetValue("/content indicators", out subTags))
            foreach (var tag in subTags.RecursiveNamespacedChildren.Values.Select(SelectTagName))
                tagSet.Add(tag);
        if (source.HasFlag(TagSource.Origin) && tags.TryGetValue("/origin", out subTags))
            foreach (var tag in subTags.RecursiveNamespacedChildren.Values.Select(SelectTagName))
                tagSet.Add(tag);

        if (source.HasAnyFlags(TagSource.SettingPlace, TagSource.SettingTimePeriod, TagSource.SettingTimeSeason) && tags.TryGetValue("/setting", out var setting)) {
            if (source.HasFlag(TagSource.SettingPlace) && setting.Children.TryGetValue("place", out var place)) {
                tagSet.AddRange(place.Children.Values.Select(SelectTagName));
            }
            if (source.HasAnyFlags(TagSource.SettingTimePeriod, TagSource.SettingTimeSeason) && setting.Children.TryGetValue("time", out var time)) {
                if (source.HasFlag(TagSource.SettingTimeSeason) && time.Children.TryGetValue("season", out var season))
                    tagSet.AddRange(season.Children.Values.Select(SelectTagName));

                if (source.HasFlag(TagSource.SettingTimePeriod)) {
                    if (time.Children.TryGetValue("present", out var present))
                        tagSet.Add(present.Children.ContainsKey("alternative present") ? "Alternative Present" : "Present");
                    if (time.Children.TryGetValue("future", out var future))
                        tagSet.AddRange(future.Children.Values.Select(SelectTagName).Prepend("Future"));
                    if (time.Children.TryGetValue("past", out var past))
                        tagSet.AddRange(past.Children.ContainsKey("alternative past")
                            ? new string[] { "Alternative Past" }
                            : past.Children.Values.Select(SelectTagName).Prepend("Historical Past")
                        );
                }
            }
        }

        var tagsToFilter = new List<ResolvedTag>();
        if (source.HasFlag(TagSource.Elements) && tags.TryGetValue("/elements", out subTags))
            tagsToFilter.AddRange(subTags.RecursiveNamespacedChildren.Values);
        if (source.HasFlag(TagSource.Fetishes) && tags.TryGetValue("/fetishes", out subTags))
            tagsToFilter.AddRange(subTags.RecursiveNamespacedChildren.Values);
        if (source.HasFlag(TagSource.Themes) && tags.TryGetValue("/themes", out subTags))
            tagsToFilter.AddRange(subTags.RecursiveNamespacedChildren.Values);

        if (source.HasFlag(TagSource.CustomTags) && tags.TryGetValue("/custom user tags", out var customTags)) {
            tagSet.AddRange(customTags.Children.Values.Where(tag => !tag.IsParent).Select(SelectTagName));

            if (source.HasFlag(TagSource.Elements) && customTags.Children.TryGetValue("elements", out subTags))
                tagsToFilter.AddRange(subTags.RecursiveNamespacedChildren.Values);
            if (source.HasFlag(TagSource.Fetishes) && customTags.Children.TryGetValue("fetishes", out subTags))
                tagsToFilter.AddRange(subTags.RecursiveNamespacedChildren.Values);
            if (source.HasFlag(TagSource.Themes) && customTags.Children.TryGetValue("themes", out subTags))
                tagsToFilter.AddRange(subTags.RecursiveNamespacedChildren.Values);
        }

        foreach (var tag in tagsToFilter)
        {
            if (tag.IsWeightless && !includeFilter.HasFlag(TagIncludeFilter.Weightless))
                continue;
            if (tag.IsLocalSpoiler && !includeFilter.HasFlag(TagIncludeFilter.LocalSpoiler))
                continue;
            if (tag.IsGlobalSpoiler && !includeFilter.HasFlag(TagIncludeFilter.GlobalSpoiler))
                continue;
            if (tag.IsParent ? !tag.IsWeightless && !includeFilter.HasFlag(TagIncludeFilter.Parent) : !includeFilter.HasFlag(TagIncludeFilter.Child))
                continue;
            if (minWeight is > TagWeight.Weightless && !tag.IsWeightless && tag.Weight < minWeight)
                continue;
            tagSet.Add(SelectTagName(tag));
        }

        return tagSet
            .Distinct()
            .ToArray();
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
            return Array.Empty<string>();

        var productionCountries = new List<string>();
        foreach (var childTag in origin.Children.Keys) {
            productionCountries.AddRange(childTag.ToLowerInvariant() switch {
                "american-japanese co-production" => new string[] {"Japan", "United States of America" },
                "chinese production" => new string[] {"China" },
                "french-chinese co-production" => new string[] {"France", "China" },
                "french-japanese co-production" => new string[] {"Japan", "France" },
                "indo-japanese co-production" => new string[] {"Japan", "India" },
                "japanese production" => new string[] {"Japan" },
                "korean-japanese co-production" => new string[] {"Japan", "Republic of Korea" },
                "north korean production" => new string[] {"Democratic People's Republic of Korea" },
                "polish-japanese co-production" => new string[] {"Japan", "Poland" },
                "russian-japanese co-production" => new string[] {"Japan", "Russia" },
                "saudi arabian-japanese co-production" => new string[] {"Japan", "Saudi Arabia" },
                "singaporean production" => new string[] {"Singapore" },
                "sino-japanese co-production" => new string[] {"Japan", "China" },
                "south korea production" => new string[] {"Republic of Korea" },
                "taiwanese production" => new string[] {"Taiwan" },
                "thai production" => new string[] {"Thailand" },
                _ => Array.Empty<string>(),
            });
        }
        return productionCountries
            .Distinct()
            .ToArray();
    }

    private static string SelectTagName(Tag tag)
        => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(tag.Name);

    private static bool HasAnyFlags(this Enum value, params Enum[] candidates)
        => candidates.Any(value.HasFlag);
}