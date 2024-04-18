using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.API;
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
        if (!Lookup.TryGetSeriesIdFor(series, out var seriesId))
            return ItemUpdateType.None;

        // Provide metadata for a series using Shoko's Group feature
        var showInfo = await ApiManager.GetShowInfoForSeries(seriesId);
        if (showInfo == null || showInfo.SeasonList.Count == 0) {
            Logger.LogWarning("Unable to find show info for series. (Series={SeriesID})", seriesId);
            return ItemUpdateType.None;
        }

        // Get the existing seasons and episode ids
        var itemUpdated = ItemUpdateType.None;
        if (Plugin.Instance.Configuration.AddMissingMetadata && options.MetadataRefreshMode != MetadataRefreshMode.ValidationOnly) {
            // Get the existing seasons and episode ids
            var seasons = series.Children
                .OfType<Season>()
                .Where(season => season.IndexNumber.HasValue)
                .ToDictionary(season => season.IndexNumber!.Value);

            var knownSeasonIds = showInfo.SeasonOrderDictionary.Select(s => s.Key).ToHashSet();
            if (showInfo.HasSpecials)
                knownSeasonIds.Add(0);

            var toRemove = seasons
                .ExceptBy(knownSeasonIds, season => season.Key)
                .Where(season => season.Value.IsVirtualItem)
                .ToList();
            foreach (var (seasonNumber, season) in toRemove) {
                Logger.LogDebug("Removing unknown Season {SeasonNumber} for Series {SeriesName} (Series={SeriesId})", seasonNumber, series.Name, seriesId);
                seasons.Remove(seasonNumber);
                LibraryManager.DeleteItem(season, new() { DeleteFileLocation = false });
            }

            // Add missing seasons
            foreach (var (seasonNumber, season) in CreateMissingSeasons(showInfo, series, seasons)) {
                itemUpdated |= ItemUpdateType.MetadataImport;
                seasons.TryAdd(seasonNumber, season);
            }

            // Specials.
            if (seasons.TryGetValue(0, out var zeroSeason)) {
                var goodKnownEpisodeIds = showInfo.SpecialsSet;
                var toRemoveEpisodes = new List<Episode>();
                var existingEpisodes = new HashSet<string>();
                foreach (var episode in zeroSeason.Children.OfType<Episode>()) {
                    if (Lookup.TryGetEpisodeIdsFor(episode, out var episodeIds))
                        if (episode.IsVirtualItem && !goodKnownEpisodeIds.Overlaps(episodeIds)) {
                            toRemoveEpisodes.Add(episode);
                        }
                        else {
                            foreach (var episodeId in episodeIds)
                                existingEpisodes.Add(episodeId);
                        }
                    else if (Lookup.TryGetEpisodeIdFor(episode, out var episodeId)) {
                        if (episode.IsVirtualItem && !goodKnownEpisodeIds.Contains(episodeId))
                            toRemoveEpisodes.Add(episode);
                        else
                            existingEpisodes.Add(episodeId);
                    }
                }

                foreach (var episode in toRemoveEpisodes) {
                    Logger.LogDebug("Removing unknown Episode {EpisodeName} from Season {SeasonNumber} for Series {SeriesName} (Series={SeriesId})", episode.Name, 0, series.Name, seriesId);
                    LibraryManager.DeleteItem(episode, new() { DeleteFileLocation = false });
                }

                foreach (var seasonInfo in showInfo.SeasonList) {
                    foreach (var episodeId in ApiManager.GetLocalEpisodeIdsForSeries(seasonInfo.Id))
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
                if (!seasons.TryGetValue(seasonNumber, out var season) || season == null)
                    continue;

                if (seasonInfo == null || !showInfo.TryGetBaseSeasonNumberForSeasonInfo(seasonInfo, out var baseSeasonNumber)) {
                    Logger.LogWarning("Unable to find series info for Season {SeasonNumber} in group for series. (Group={GroupId})", seasonNumber, showInfo.GroupId);
                    return ItemUpdateType.None;
                }
                var offset = Math.Abs(seasonNumber - baseSeasonNumber);

                var episodeList = offset == 0 ? seasonInfo.EpisodeList : seasonInfo.AlternateEpisodesList;
                var goodKnownEpisodeIds = episodeList
                    .Select(episodeInfo => episodeInfo.Id)
                    .ToHashSet();
                var toRemoveEpisodes = new List<Episode>();
                var existingEpisodes = new HashSet<string>();
                foreach (var episode in season.Children.OfType<Episode>()) {
                    if (Lookup.TryGetEpisodeIdsFor(episode, out var episodeIds))
                        if (episode.IsVirtualItem && !goodKnownEpisodeIds.Overlaps(episodeIds)) {
                            toRemoveEpisodes.Add(episode);
                        }
                        else {
                            foreach (var episodeId in episodeIds)
                                existingEpisodes.Add(episodeId);
                        }
                    else if (Lookup.TryGetEpisodeIdFor(episode, out var episodeId)) {
                        if (episode.IsVirtualItem && !goodKnownEpisodeIds.Contains(episodeId))
                            toRemoveEpisodes.Add(episode);
                        else
                            existingEpisodes.Add(episodeId);
                    }
                }

                foreach (var episode in toRemoveEpisodes) {
                    Logger.LogDebug("Removing unknown Episode {EpisodeName} from Season {SeasonNumber} for Series {SeriesName} (Series={SeriesId})", episode.Name, seasonNumber, series.Name, seriesId);
                    LibraryManager.DeleteItem(episode, new() { DeleteFileLocation = false });
                }

                foreach (var episodeId in ApiManager.GetLocalEpisodeIdsForSeries(seasonInfo.Id))
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