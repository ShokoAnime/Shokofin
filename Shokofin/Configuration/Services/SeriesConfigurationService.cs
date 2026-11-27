
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Models;
using Shokofin.Utils;

namespace Shokofin.Configuration;

public class SeriesConfigurationService(ILogger<SeriesConfigurationService> logger, ShokoApiClient apiClient, ShokoApiManager apiManager) {

    private readonly GuardedMemoryCache _cache = new(
        logger,
        new() { ExpirationScanFrequency = Plugin.Instance.Configuration.Debug.ExpirationScanFrequency },
        new() { AbsoluteExpirationRelativeToNow = Plugin.Instance.Configuration.Debug.AbsoluteExpirationRelativeToNow }
    );

    private const string ManagedBy = "This tag is managed by Shokofin and should not be edited manually.";

    private readonly IReadOnlyList<SimpleTag> _simpleTags = [
        new() {
            NameRegex = new(@"^Series Type/Unknowns?$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Series Type/Unknown",
            Description = $"Override the series type as an unknown type series. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Series Type/Others?$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Series Type/Other",
            Description = $"Override the series type as an other type series. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Series Type/TVs?$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Series Type/TV",
            Description = $"Override the series type as a TV series. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Series Type/TV ?Specials?$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Series Type/TV Special",
            Description = $"Override the series type as a TV special. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Series Type/Webs?$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Series Type/Web",
            Description = $"Override the series type as a web series. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Series Type/Movies?$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Series Type/Movie",
            Description = $"Override the series type as a movie series. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Series Type/OVAs?$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Series Type/OVA",
            Description = $"Override the series type as an original video animation series. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Series Type/Music ?Videos?$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Series Type/Music Video",
            Description = $"Override the series type as a music video series. {ManagedBy}",
        },

        new() {
            NameRegex = new(@"^Shokofin/(AniDB Structure|Structure/AniDB)$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Shokofin/Structure/AniDB",
            Description = $"Use an AniDB based structure for this Shoko series in Jellyfin. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Shokofin/(Shoko Structure|Structure/Shoko)$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Shokofin/Structure/Shoko",
            Description = $"Use a Shoko Group based structure for this Shoko series in Jellyfin. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Shokofin/(TMDb Structure|Structure/TMDb)$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Shokofin/Structure/TMDb",
            Description = $"Use a TMDb based structure for this Shoko series in Jellyfin. {ManagedBy}",
        },

        new() {
            NameRegex = new(@"^Shokofin/(Default Season Ordering|Season Ordering/Default)$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Shokofin/Season Ordering/Default",
            Description = $"Let the server decide the season ordering. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Shokofin/(Release Based Season Ordering|Season Ordering/Release)$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Shokofin/Season Ordering/Release",
            Description = $"Order seasons based on release date. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Shokofin/(Chronological Season Ordering|Season Ordering/Chronological)$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Shokofin/Season Ordering/Chronological",
            Description = $"Order seasons in chronological order with indirect relations weighting in on the position of each season. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Shokofin/(Simplified Chronological Season Ordering|Season Ordering/Simplified Chronological)$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Shokofin/Season Ordering/Simplified Chronological",
            Description = $"Order seasons in chronological order while ignoring indirect relations. {ManagedBy}",
        },

        new() {
            Name = "Shokofin/Specials Placement/Excluded",
            Description = $"Always exclude the specials from the season. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Specials Placement/After Season",
            Description = $"Always place the specials after the normal episodes in the season. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Specials Placement/Mixed",
            Description = $"Place the specials in-between normal episodes based upon data from TMDb or when the episodes aired. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Specials Placement/Air Date",
            Description = $"Place the specials in-between normal episodes based on when the episodes aired. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Specials Placement/TMDb",
            Description = $"Place the specials in-between normal episodes based upon data from TMDb. {ManagedBy}",
        },

        new() {
            NameRegex = new(@"^Shokofin/(No Merge|Merge/None)$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Shokofin/Merge/None",
            Description = $"Never merge this series with other series when deciding on what to merge for seasons in Jellyfin. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Shokofin/Merge[ /]Forward$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Shokofin/Merge/Forward",
            Description = $"Merge the current series with the sequel series in Jellyfin. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Shokofin/Merge[ /]Backward$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Shokofin/Merge/Backward",
            Description = $"Merge the current series with the prequel series in Jellyfin. {ManagedBy}",
        },
        new() {
            NameRegex = new(@"^Shokofin/Merge( with |/)Main Story$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Compiled),
            Name = "Shokofin/Merge/Main Story",
            Description = $"Merge the current side-story with the main-story in Jellyfin. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Merge/Group/A/Target",
            Description = $"Merge the current series with one or more other series in merge group A in Jellyfin. All other series that should be merged into this series need to have the \"Shokofin/Merge/Group/A/Source\" tag set. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Merge/Group/A/Source",
            Description = $"Merge the current series into the main series in merge group A in Jellyfin. The main series needs to have the \"Shokofin/Merge/Group/A/Target\" tag set. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Merge/Group/B/Target",
            Description = $"Merge the current series with one or more other series in merge group B in Jellyfin. All other series that should be merged into this series need to have the \"Shokofin/Merge/Group/B/Source\" tag set. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Merge/Group/B/Source",
            Description = $"Merge the current series into the main series in merge group B in Jellyfin. The main series needs to have the \"Shokofin/Merge/Group/B/Target\" tag set. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Merge/Group/C/Target",
            Description = $"Merge the current series with one or more other series in merge group C in Jellyfin. All other series that should be merged into this series need to have the \"Shokofin/Merge/Group/C/Source\" tag set. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Merge/Group/C/Source",
            Description = $"Merge the current series into the main series in merge group C in Jellyfin. The main series needs to have the \"Shokofin/Merge/Group/C/Target\" tag set. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Merge/Group/D/Target",
            Description = $"Merge the current series with one or more other series in merge group D in Jellyfin. All other series that should be merged into this series need to have the \"Shokofin/Merge/Group/D/Source\" tag set. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Merge/Group/D/Source",
            Description = $"Merge the current series into the main series in merge group D in Jellyfin. The main series needs to have the \"Shokofin/Merge/Group/D/Target\" tag set. {ManagedBy}",
        },

        new() {
            Name = "Shokofin/Episodes as Specials",
            Description = $"Converts normal episodes to specials in Jellyfin. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Specials as Episodes",
            Description = $"Converts specials to normal episodes in Jellyfin. {ManagedBy}",
        },
        new() {
            Name = "Shokofin/Specials As Extra Featurettes",
            Description = $"Always convert specials to extra featurettes in Jellyfin. {ManagedBy}",
        },

        new() {
            Name = "Shokofin/Order by AirDate",
            Description = $"Order episodes by air date instead of episode number in Jellyfin. {ManagedBy}",
        },

        new() {
            Name = "Shokofin/Include in Playlists",
            Description = $"Always include this series in automatic playlist generation in Jellyfin. {ManagedBy}",
        },
    ];

    private Task<IReadOnlyDictionary<string, int>> CreatOrGetRequiredTags()
        => _cache.GetOrCreateAsync<IReadOnlyDictionary<string, int>>("tags", async () => {
            var allCustomTags = await apiClient.GetCustomTags();
            var outputDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var simpleTag in _simpleTags) {
                var localTags = allCustomTags
                    .Where(x => simpleTag.NameRegex is not null
                        ? simpleTag.NameRegex.IsMatch(x.Name)
                        : string.Equals(simpleTag.Name, x.Name, StringComparison.OrdinalIgnoreCase)
                    )
                    .ToList();
                if (localTags.Count == 0) {
                    var newTag = await apiClient.CreateCustomTag(simpleTag.Name, simpleTag.Description);
                    outputDict[simpleTag.Key] = newTag.Id;
                    continue;
                }

                var existingTag = localTags[0];
                if (
                    !string.Equals(simpleTag.Name, existingTag.Name, StringComparison.Ordinal) ||
                    !string.Equals(simpleTag.Description, existingTag.Description, StringComparison.Ordinal)
                ) {
                    existingTag = await apiClient.UpdateCustomTag(existingTag.Id, simpleTag.Name, simpleTag.Description);
                }

                if (localTags.Skip(1).ToList() is { Count: > 0 } otherTags) {
                    var seriesIds = await apiClient.GetSeriesIdsWithCustomTag(otherTags.Select(x => x.Id));
                    foreach (var otherTag in otherTags) {
                        await apiClient.RemoveCustomTag(otherTag.Id);
                    }
                    foreach (var seriesId in seriesIds) {
                        await apiClient.AddCustomTagToShokoSeries(seriesId, existingTag.Id);
                    }
                }

                outputDict[simpleTag.Key] = existingTag.Id;
            }
            return outputDict;
        });

    public async Task<SeriesConfiguration?> GetSeriesConfigurationForId(int shokoSeriesId) {
        if (await apiClient.GetShokoSeries(shokoSeriesId.ToString()) is not { })
            return null;

        return await apiManager.GetInternalSeriesConfiguration(shokoSeriesId.ToString());
    }

    public async Task<SeriesConfiguration> UpdateSeriesConfigurationForId(int shokoSeriesId, NullableSeriesConfiguration seriesConfiguration) {
        var config = await GetSeriesConfigurationForId(shokoSeriesId) ??
            throw new InvalidOperationException("Series not found.");

        if (seriesConfiguration.Type is not null)
            config.Type = seriesConfiguration.Type.Value;
        if (seriesConfiguration.StructureType is not null)
            config.StructureType = seriesConfiguration.StructureType.Value;
        if (seriesConfiguration.SeasonOrdering is not null)
            config.SeasonOrdering = seriesConfiguration.SeasonOrdering.Value;
        if (seriesConfiguration.SpecialsPlacement is not null)
            config.SpecialsPlacement = seriesConfiguration.SpecialsPlacement.Value;
        if (seriesConfiguration.SeasonMergingBehavior is not null)
            config.SeasonMergingBehavior = seriesConfiguration.SeasonMergingBehavior.Value;
        if (seriesConfiguration.EpisodeConversion is not null)
            config.EpisodeConversion = seriesConfiguration.EpisodeConversion.Value;
        if (seriesConfiguration.OrderByAirdate is not null)
            config.OrderByAirdate = seriesConfiguration.OrderByAirdate.Value;
        if (seriesConfiguration.PlaylistInclude is not null)
            config.PlaylistInclude = seriesConfiguration.PlaylistInclude.Value;

        return await UpdateSeriesConfigurationForId(shokoSeriesId, config);
    }

    public async Task<SeriesConfiguration> UpdateSeriesConfigurationForId(int shokoSeriesId, SeriesConfiguration seriesConfiguration) {
        if (await apiClient.GetShokoSeries(shokoSeriesId.ToString()) is not { } series)
            throw new InvalidOperationException("Series not found.");

        var toAddSet = new HashSet<int>();
        var toRemoveSet = new HashSet<int>();
        var knownTagDict = await CreatOrGetRequiredTags();
        var currentTagSet = await apiClient.GetCustomTagsForShokoSeries(shokoSeriesId)
            .ContinueWith(x => x.Result.Select(x => x.Id).ToHashSet())
            ;

        var seriesTypes = knownTagDict.Where(x => x.Key.StartsWith("/series type/")).ToDictionary(x => x.Key, x => x.Value);
        foreach (var (_, id) in seriesTypes)
            toRemoveSet.Add(id);
        switch (seriesConfiguration.Type) {
            case SeriesType.None:
                break;
            case SeriesType.TVSpecial:
                toRemoveSet.Remove(knownTagDict["/series type/tv special"]);
                toAddSet.Add(knownTagDict["/series type/tv special"]);
                break;
            case SeriesType.MusicVideo:
                toRemoveSet.Remove(knownTagDict["/series type/music video"]);
                toAddSet.Add(knownTagDict["/series type/music video"]);
                break;
            default:
                toRemoveSet.Remove(knownTagDict[$"/series type/{seriesConfiguration.Type.ToString().ToLower()}"]);
                toAddSet.Add(knownTagDict[$"/series type/{seriesConfiguration.Type.ToString().ToLower()}"]);
                break;
        }

        var structureTypes = knownTagDict.Where(x => x.Key.Contains("structure")).ToDictionary(x => x.Key, x => x.Value);
        foreach (var (_, id) in structureTypes)
            toRemoveSet.Add(id);
        switch (seriesConfiguration.StructureType) {
            case SeriesStructureType.TMDB_SeriesAndMovies:
                toRemoveSet.Remove(knownTagDict["/shokofin/structure/tmdb"]);
                toAddSet.Add(knownTagDict["/shokofin/structure/tmdb"]);
                break;
            case SeriesStructureType.AniDB_Anime:
                toRemoveSet.Remove(knownTagDict["/shokofin/structure/anidb"]);
                toAddSet.Add(knownTagDict["/shokofin/structure/anidb"]);
                break;
            case SeriesStructureType.Shoko_Groups:
                toRemoveSet.Remove(knownTagDict["/shokofin/structure/shoko"]);
                toAddSet.Add(knownTagDict["/shokofin/structure/shoko"]);
                break;
        }

        var seasonOrderingTypes = knownTagDict.Where(x => x.Key.Contains("season ordering")).ToDictionary(x => x.Key, x => x.Value);
        foreach (var (_, id) in seasonOrderingTypes)
            toRemoveSet.Add(id);
        switch (seriesConfiguration.SeasonOrdering) {
            case Ordering.OrderType.None:
                break;
            case Ordering.OrderType.Default:
                toRemoveSet.Remove(knownTagDict["/shokofin/season ordering/default"]);
                toAddSet.Add(knownTagDict["/shokofin/season ordering/default"]);
                break;
            case Ordering.OrderType.ReleaseDate:
                toRemoveSet.Remove(knownTagDict["/shokofin/season ordering/release"]);
                toAddSet.Add(knownTagDict["/shokofin/season ordering/release"]);
                break;
            case Ordering.OrderType.Chronological:
                toRemoveSet.Remove(knownTagDict["/shokofin/season ordering/chronological"]);
                toAddSet.Add(knownTagDict["/shokofin/season ordering/chronological"]);
                break;
            case Ordering.OrderType.ChronologicalIgnoreIndirect:
                toRemoveSet.Remove(knownTagDict["/shokofin/season ordering/simplified chronological"]);
                toAddSet.Add(knownTagDict["/shokofin/season ordering/simplified chronological"]);
                break;
        }

        var specialsPlacementTypes = knownTagDict.Where(x => x.Key.Contains("specials placement")).ToDictionary(x => x.Key, x => x.Value);
        foreach (var (_, id) in specialsPlacementTypes)
            toRemoveSet.Add(id);
        switch (seriesConfiguration.SpecialsPlacement) {
            case Ordering.SpecialOrderType.None:
                break;
            case Ordering.SpecialOrderType.Excluded:
                toRemoveSet.Remove(knownTagDict["/shokofin/specials placement/excluded"]);
                toAddSet.Add(knownTagDict["/shokofin/specials placement/excluded"]);
                break;
            case Ordering.SpecialOrderType.AfterSeason:
                toRemoveSet.Remove(knownTagDict["/shokofin/specials placement/after season"]);
                toAddSet.Add(knownTagDict["/shokofin/specials placement/after season"]);
                break;
            case Ordering.SpecialOrderType.InBetweenSeasonMixed:
                toRemoveSet.Remove(knownTagDict["/shokofin/specials placement/mixed"]);
                toAddSet.Add(knownTagDict["/shokofin/specials placement/mixed"]);
                break;
            case Ordering.SpecialOrderType.InBetweenSeasonByAirDate:
                toRemoveSet.Remove(knownTagDict["/shokofin/specials placement/air date"]);
                toAddSet.Add(knownTagDict["/shokofin/specials placement/air date"]);
                break;
            case Ordering.SpecialOrderType.InBetweenSeasonByOtherData:
                toRemoveSet.Remove(knownTagDict["/shokofin/specials placement/tmdb"]);
                toAddSet.Add(knownTagDict["/shokofin/specials placement/tmdb"]);
                break;
        }

        var mergeTypes = knownTagDict.Where(x => x.Key.Contains("merge")).ToDictionary(x => x.Key, x => x.Value);
        foreach (var (_, id) in mergeTypes)
            toRemoveSet.Add(id);
        if (seriesConfiguration.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.NoMerge)) {
            toRemoveSet.Remove(knownTagDict["/shokofin/merge/none"]);
            toAddSet.Add(knownTagDict["/shokofin/merge/none"]);
        }
        else {
            if (seriesConfiguration.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeForward)) {
                toRemoveSet.Remove(knownTagDict["/shokofin/merge/forward"]);
                toAddSet.Add(knownTagDict["/shokofin/merge/forward"]);
            }
            if (seriesConfiguration.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeBackward)) {
                toRemoveSet.Remove(knownTagDict["/shokofin/merge/backward"]);
                toAddSet.Add(knownTagDict["/shokofin/merge/backward"]);
            }

            if (seriesConfiguration.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeWithMainStory)) {
                toRemoveSet.Remove(knownTagDict["/shokofin/merge/main story"]);
                toAddSet.Add(knownTagDict["/shokofin/merge/main story"]);
            }

            if (seriesConfiguration.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupASource)) {
                toRemoveSet.Remove(knownTagDict["/shokofin/merge/group/a/source"]);
                toAddSet.Add(knownTagDict["/shokofin/merge/group/a/source"]);
            }
            else if (seriesConfiguration.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupATarget)) {
                toRemoveSet.Remove(knownTagDict["/shokofin/merge/group/a/target"]);
                toAddSet.Add(knownTagDict["/shokofin/merge/group/a/target"]);
            }

            if (seriesConfiguration.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupBSource)) {
                toRemoveSet.Remove(knownTagDict["/shokofin/merge/group/b/source"]);
                toAddSet.Add(knownTagDict["/shokofin/merge/group/b/source"]);
            }
            else if (seriesConfiguration.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupBTarget)) {
                toRemoveSet.Remove(knownTagDict["/shokofin/merge/group/b/target"]);
                toAddSet.Add(knownTagDict["/shokofin/merge/group/b/target"]);
            }

            if (seriesConfiguration.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupCSource)) {
                toRemoveSet.Remove(knownTagDict["/shokofin/merge/group/c/source"]);
                toAddSet.Add(knownTagDict["/shokofin/merge/group/c/source"]);
            }
            else if (seriesConfiguration.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupCTarget)) {
                toRemoveSet.Remove(knownTagDict["/shokofin/merge/group/c/target"]);
                toAddSet.Add(knownTagDict["/shokofin/merge/group/c/target"]);
            }

            if (seriesConfiguration.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupDSource)) {
                toRemoveSet.Remove(knownTagDict["/shokofin/merge/group/d/source"]);
                toAddSet.Add(knownTagDict["/shokofin/merge/group/d/source"]);
            }
            else if (seriesConfiguration.SeasonMergingBehavior.HasFlag(SeasonMergingBehavior.MergeGroupDTarget)) {
                toRemoveSet.Remove(knownTagDict["/shokofin/merge/group/d/target"]);
                toAddSet.Add(knownTagDict["/shokofin/merge/group/d/target"]);
            }
        }

        var episodeConversions = knownTagDict.Where(x => x.Key.Contains(" as ")).ToDictionary(x => x.Key, x => x.Value);
        foreach (var (_, id) in episodeConversions)
            toRemoveSet.Add(id);
        switch (seriesConfiguration.EpisodeConversion) {
            case SeriesEpisodeConversion.EpisodesAsSpecials:
                toRemoveSet.Remove(knownTagDict["/shokofin/episodes as specials"]);
                toAddSet.Add(knownTagDict["/shokofin/episodes as specials"]);
                break;
            case SeriesEpisodeConversion.SpecialsAsEpisodes:
                toRemoveSet.Remove(knownTagDict["/shokofin/specials as episodes"]);
                toAddSet.Add(knownTagDict["/shokofin/specials as episodes"]);
                break;
            case SeriesEpisodeConversion.SpecialsAsExtraFeaturettes:
                toRemoveSet.Remove(knownTagDict["/shokofin/specials as extra featurettes"]);
                toAddSet.Add(knownTagDict["/shokofin/specials as extra featurettes"]);
                break;
        }

        if (seriesConfiguration.OrderByAirdate) {
            toAddSet.Add(knownTagDict["/shokofin/order by airdate"]);
        }
        else {
            toRemoveSet.Add(knownTagDict["/shokofin/order by airdate"]);
        }

        if (seriesConfiguration.PlaylistInclude) {
            toAddSet.Add(knownTagDict["/shokofin/include in playlists"]);
        }
        else {
            toRemoveSet.Add(knownTagDict["/shokofin/include in playlists"]);
        }

        toAddSet.ExceptWith(currentTagSet);
        toRemoveSet.IntersectWith(currentTagSet);

        foreach (var tagToRemove in toRemoveSet)
            await apiClient.RemoveCustomTagFromShokoSeries(shokoSeriesId, tagToRemove);
        foreach (var tagToAdd in toAddSet)
            await apiClient.AddCustomTagToShokoSeries(shokoSeriesId, tagToAdd);

        return seriesConfiguration;
    }

    /// <summary>
    /// A simple tag with a name and description. Used to determine which tags
    /// exist in Shoko, and which tags need to be created.
    /// </summary>
    class SimpleTag {
        /// <summary>
        /// Regular expression to match against the name, if any.
        /// </summary>
        public Regex? NameRegex { get; init; }

        /// <summary>
        /// Properly cased name of the tag.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Proper description of the tag.
        /// </summary>
        public required string Description { get; init; }

        /// <summary>
        /// The namespaced key for the tag.
        /// </summary>
        public string Key => $"/{Name.ToLower()}";
    }
}
