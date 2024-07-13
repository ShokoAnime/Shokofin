using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.ExternalIds;
using Shokofin.Utils;

using Info = Shokofin.API.Info;

namespace Shokofin.Providers;

public class CustomSeriesProvider : ICustomMetadataProvider<Series>
{
    public string Name => Plugin.MetadataProviderName;

    private readonly ILogger<CustomSeriesProvider> Logger;

    private readonly ShokoAPIManager ApiManager;

    private readonly IIdLookup Lookup;

    private readonly ILibraryManager LibraryManager;

    private static bool ShouldAddMetadata => Plugin.Instance.Configuration.AddMissingMetadata;

    public CustomSeriesProvider(ILogger<CustomSeriesProvider> logger, ShokoAPIManager apiManager, IIdLookup lookup, ILibraryManager libraryManager)
    {
        Logger = logger;
        ApiManager = apiManager;
        Lookup = lookup;
        LibraryManager = libraryManager;
    }

    public async Task<ItemUpdateType> FetchAsync(Series series, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        // Abort if we're unable to get the shoko series id
        if (!series.TryGetProviderId(ShokoSeriesId.Name, out var seriesId))
            return ItemUpdateType.None;

        var trackerId = Plugin.Instance.Tracker.Add($"Providing custom info for Series \"{series.Name}\". (Series=\"{seriesId}\")");
        try {
            // Provide metadata for a series using Shoko's Group feature
            var showInfo = await ApiManager.GetShowInfoForSeries(seriesId);
            if (showInfo == null || showInfo.SeasonList.Count == 0) {
                Logger.LogWarning("Unable to find show info for series. (Series={SeriesID})", seriesId);
                return ItemUpdateType.None;
            }

            // Get the existing seasons and known seasons.
            var itemUpdated = ItemUpdateType.None;
            var allSeasons = series.Children
                .OfType<Season>()
                .Where(season => season.IndexNumber.HasValue)
                .ToList();
            var seasons = allSeasons
                .OrderBy(season => season.IndexNumber!.Value)
                .ThenBy(season => season.IsVirtualItem)
                .ThenBy(season => season.Path)
                .GroupBy(season => season.IndexNumber!.Value)
                .ToDictionary(groupBy => groupBy.Key, groupBy => groupBy.First());
            var extraSeasonsToRemove = allSeasons
                .Except(seasons.Values)
                .ToList();
            var knownSeasonIds = ShouldAddMetadata
                ? showInfo.SeasonOrderDictionary.Keys.ToHashSet()
                : showInfo.SeasonOrderDictionary
                    .Where(pair => !pair.Value.IsEmpty(Math.Abs(pair.Key - showInfo.GetBaseSeasonNumberForSeasonInfo(pair.Value))))
                    .Select(pair => pair.Key)
                    .ToHashSet();
            if (ShouldAddMetadata ? showInfo.HasSpecials : showInfo.HasSpecialsWithFiles)
                knownSeasonIds.Add(0);

            // Remove unknown or unwanted seasons.
            var toRemoveSeasons = seasons.ExceptBy(knownSeasonIds, season => season.Key)
                .Where(season => string.IsNullOrEmpty(season.Value.Path) || season.Value.IsVirtualItem)
                .ToList();
            foreach (var (seasonNumber, season) in toRemoveSeasons) {
                Logger.LogDebug("Removing Season {SeasonNumber} for Series {SeriesName} (Series={SeriesId})", seasonNumber, series.Name, seriesId);
                seasons.Remove(seasonNumber);
                LibraryManager.DeleteItem(season, new() { DeleteFileLocation = false });
            }

            foreach (var season in extraSeasonsToRemove) {
                if (seasons.TryGetValue(season.IndexNumber!.Value, out var mainSeason)) {
                    var episodes = season.Children
                        .OfType<Episode>()
                        .Where(episode => !string.IsNullOrEmpty(episode.Path) && episode.ParentId == season.Id)
                        .ToList();
                    foreach (var episode in episodes) {
                        Logger.LogInformation("Updating parent of physical episode {EpisodeNumber} {EpisodeName} in Season {SeasonNumber} for {SeriesName} (Series={SeriesId})", episode.IndexNumber, episode.Name, season.IndexNumber, series.Name, seriesId);
                        episode.SetParent(mainSeason);
                    }
                    await LibraryManager.UpdateItemsAsync(episodes, mainSeason, ItemUpdateType.MetadataEdit, CancellationToken.None);
                }

                Logger.LogDebug("Removing extra Season {SeasonNumber} for Series {SeriesName} (Series={SeriesId})", season.IndexNumber!.Value, series.Name, seriesId);
                LibraryManager.DeleteItem(season, new() { DeleteFileLocation = false });
            }

            // Add missing seasons.
            if (ShouldAddMetadata && options.MetadataRefreshMode != MetadataRefreshMode.ValidationOnly) 
                foreach (var (seasonNumber, season) in CreateMissingSeasons(showInfo, series, seasons)) {
                    itemUpdated |= ItemUpdateType.MetadataImport;
                    seasons.TryAdd(seasonNumber, season);
                }

            // Special handling of Specials (pun intended).
            if (seasons.TryGetValue(0, out var zeroSeason)) {
                // Get known episodes, existing episodes, and episodes to remove.
                var knownEpisodeIds = ShouldAddMetadata
                    ? showInfo.SpecialsDict.Keys.ToHashSet()
                    : showInfo.SpecialsDict
                        .Where(pair => pair.Value)
                        .Select(pair => pair.Key)
                        .ToHashSet();
                var existingEpisodes = new HashSet<string>();
                var toRemoveEpisodes = new List<Episode>();
                foreach (var episode in zeroSeason.Children.OfType<Episode>()) {
                    if (Lookup.TryGetEpisodeIdsFor(episode, out var episodeIds))
                        if ((string.IsNullOrEmpty(episode.Path) || episode.IsVirtualItem) && !knownEpisodeIds.Overlaps(episodeIds))
                            toRemoveEpisodes.Add(episode);
                        else 
                            foreach (var episodeId in episodeIds)
                                existingEpisodes.Add(episodeId);
                    else if (Lookup.TryGetEpisodeIdFor(episode, out var episodeId)) {
                        if ((string.IsNullOrEmpty(episode.Path) || episode.IsVirtualItem) && !knownEpisodeIds.Contains(episodeId))
                            toRemoveEpisodes.Add(episode);
                        else
                            existingEpisodes.Add(episodeId);
                    }
                }

                // Remove unknown or unwanted episodes.
                foreach (var episode in toRemoveEpisodes) {
                    Logger.LogDebug("Removing Episode {EpisodeName} from Season {SeasonNumber} for Series {SeriesName} (Series={SeriesId})", episode.Name, 0, series.Name, seriesId);
                    LibraryManager.DeleteItem(episode, new() { DeleteFileLocation = false });
                }

                // Add missing episodes.
                if (ShouldAddMetadata && options.MetadataRefreshMode != MetadataRefreshMode.ValidationOnly)
                    foreach (var seasonInfo in showInfo.SeasonList) {
                        foreach (var episodeId in await ApiManager.GetLocalEpisodeIdsForSeason(seasonInfo))
                            existingEpisodes.Add(episodeId);

                        foreach (var episodeInfo in seasonInfo.SpecialsList) {
                            if (existingEpisodes.Contains(episodeInfo.Id))
                                continue;

                            if (CustomEpisodeProvider.AddVirtualEpisode(LibraryManager, Logger, showInfo, seasonInfo, episodeInfo, zeroSeason, series))
                                itemUpdated |= ItemUpdateType.MetadataImport;
                        }
                    }
            }

            // All other seasons.
            foreach (var (seasonNumber, seasonInfo) in showInfo.SeasonOrderDictionary) {
                // Silently continue if the season doesn't exist.
                if (!seasons.TryGetValue(seasonNumber, out var season) || season == null)
                    continue;

                // Loudly skip if the season metadata doesn't exist.
                if (seasonInfo == null || !showInfo.TryGetBaseSeasonNumberForSeasonInfo(seasonInfo, out var baseSeasonNumber)) {
                    Logger.LogWarning("Unable to find series info for Season {SeasonNumber} in group for series. (Group={GroupId})", seasonNumber, showInfo.GroupId);
                    continue;
                }

                // Get known episodes, existing episodes, and episodes to remove.
                var episodeList = Math.Abs(seasonNumber - baseSeasonNumber) == 0 ? seasonInfo.EpisodeList : seasonInfo.AlternateEpisodesList;
                var knownEpisodeIds = ShouldAddMetadata ? episodeList.Select(episodeInfo => episodeInfo.Id).ToHashSet() : new HashSet<string>();
                var existingEpisodes = new HashSet<string>();
                var toRemoveEpisodes = new List<Episode>();
                foreach (var episode in season.Children.OfType<Episode>()) {
                    if (Lookup.TryGetEpisodeIdsFor(episode, out var episodeIds))
                        if ((string.IsNullOrEmpty(episode.Path) || episode.IsVirtualItem) && !knownEpisodeIds.Overlaps(episodeIds))
                            toRemoveEpisodes.Add(episode);
                        else
                            foreach (var episodeId in episodeIds)
                                existingEpisodes.Add(episodeId);
                    else if (Lookup.TryGetEpisodeIdFor(episode, out var episodeId)) {
                        if ((string.IsNullOrEmpty(episode.Path) || episode.IsVirtualItem) && !knownEpisodeIds.Contains(episodeId))
                            toRemoveEpisodes.Add(episode);
                        else
                            existingEpisodes.Add(episodeId);
                    }
                }

                // Remove unknown or unwanted episodes.
                foreach (var episode in toRemoveEpisodes) {
                    Logger.LogDebug("Removing Episode {EpisodeName} from Season {SeasonNumber} for Series {SeriesName} (Series={SeriesId})", episode.Name, seasonNumber, series.Name, seriesId);
                    LibraryManager.DeleteItem(episode, new() { DeleteFileLocation = false });
                }

                // Add missing episodes.
                if (ShouldAddMetadata && options.MetadataRefreshMode != MetadataRefreshMode.ValidationOnly) {
                    foreach (var episodeId in await ApiManager.GetLocalEpisodeIdsForSeason(seasonInfo))
                        existingEpisodes.Add(episodeId);

                    foreach (var episodeInfo in seasonInfo.EpisodeList.Concat(seasonInfo.AlternateEpisodesList)) {
                        var episodeParentIndex = Ordering.GetSeasonNumber(showInfo, seasonInfo, episodeInfo);
                        if (episodeParentIndex != seasonNumber)
                            continue;

                        if (existingEpisodes.Contains(episodeInfo.Id))
                            continue;

                        if (CustomEpisodeProvider.AddVirtualEpisode(LibraryManager, Logger, showInfo, seasonInfo, episodeInfo, season, series))
                            itemUpdated |= ItemUpdateType.MetadataImport;
                    }
                }
            }

            return itemUpdated;
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    private IEnumerable<(int, Season)> CreateMissingSeasons(Info.ShowInfo showInfo, Series series, Dictionary<int, Season> seasons)
    {
        foreach (var (seasonNumber, seasonInfo) in showInfo.SeasonOrderDictionary) {
            if (seasons.ContainsKey(seasonNumber))
                continue;
            var offset = seasonNumber - showInfo.GetBaseSeasonNumberForSeasonInfo(seasonInfo);
            var season = CustomSeasonProvider.AddVirtualSeason(LibraryManager, Logger, seasonInfo, offset, seasonNumber, series);
            if (season == null)
                continue;
            yield return (seasonNumber, season);
        }

        if (showInfo.HasSpecials && !seasons.ContainsKey(0)) {
            var season = CustomSeasonProvider.AddVirtualSeasonZero(LibraryManager, Logger, series);
            if (season != null)
                yield return (0, season);
        }
    }
}