using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Extensions;
using Shokofin.ExternalIds;
using Shokofin.MergeVersions;
using Shokofin.Utils;

using Info = Shokofin.API.Info;

namespace Shokofin.Providers;
#pragma warning disable IDE0059
#pragma warning disable IDE0290

/// <summary>
/// The custom series provider. Responsible for de-duplicating seasons,
/// adding/removing "missing" episodes, and de-duplicating physical episodes.
/// </summary>
/// <remarks>
/// This needs to be it's own class because of internal Jellyfin shenanigans
/// about how a provider cannot also be a custom provider otherwise it won't
/// save the metadata.
/// </remarks>
public class CustomSeriesProvider(ILogger<CustomSeriesProvider> _logger, ShokoApiManager _apiManager, ShokoIdLookup _lookup, ILibraryManager _libraryManager, MergeVersionsManager _mergeVersionsManager) : IHasItemChangeMonitor, ICustomMetadataProvider<Series> {
    private static bool ShouldAddMetadata => Plugin.Instance.Configuration.AddMissingMetadata;

    public string Name => Plugin.MetadataProviderName;

    public bool HasChanged(BaseItem item, IDirectoryService directoryService) {
        // We're only interested in series.
        if (item is not Series series)
            return false;

        // Abort if we're unable to get the shoko series id.
        if (!series.TryGetSeasonId(out var seasonId))
            return false;

        return true;
    }

    public async Task<ItemUpdateType> FetchAsync(Series series, MetadataRefreshOptions options, CancellationToken cancellationToken) {
        // Abort if we're unable to get the shoko series id.
        if (!_lookup.IsEnabledForItem(series) || !series.TryGetSeasonId(out var seasonId))
            return ItemUpdateType.None;

        var trackerId = Plugin.Instance.Tracker.Add($"Providing custom info for Series \"{series.Name}\". (MainSeason=\"{seasonId}\")");
        try {
            // Provide metadata for a series using Shoko's Group feature
            var showInfo = await _apiManager.GetShowInfoBySeasonId(seasonId).ConfigureAwait(false);
            if (showInfo == null || showInfo.SeasonList.Count == 0) {
                _logger.LogWarning("Unable to find show info for series. (MainSeason={MainSeasonId})", seasonId);
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
                _logger.LogDebug("Removing Season {SeasonNumber} for Series {SeriesName} (MainSeason={MainSeasonId})", seasonNumber, series.Name, seasonId);
                seasons.Remove(seasonNumber);
                _libraryManager.DeleteItem(season, new() { DeleteFileLocation = false });
            }

            foreach (var season in extraSeasonsToRemove) {
                if (seasons.TryGetValue(season.IndexNumber!.Value, out var mainSeason)) {
                    var episodes = season.Children
                        .OfType<Episode>()
                        .Where(episode => !string.IsNullOrEmpty(episode.Path) && episode.ParentId == season.Id)
                        .ToList();
                    foreach (var episode in episodes) {
                        _logger.LogInformation("Updating parent of physical episode {EpisodeNumber} {EpisodeName} in Season {SeasonNumber} for {SeriesName} (MainSeason={MainSeasonId})", episode.IndexNumber, episode.Name, season.IndexNumber, series.Name, seasonId);
                        episode.SetParent(mainSeason);
                    }
                    await _libraryManager.UpdateItemsAsync(episodes, mainSeason, ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
                }

                _logger.LogDebug("Removing extra Season {SeasonNumber} for Series {SeriesName} (MainSeason={MainSeasonId})", season.IndexNumber!.Value, series.Name, seasonId);
                _libraryManager.DeleteItem(season, new() { DeleteFileLocation = false });
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
                var orderedEpisodes = zeroSeason.Children.OfType<Episode>().OrderBy(e => e.IndexNumber).ThenBy(e => e.IndexNumberEnd).ThenByDescending(e => e.IsVirtualItem).ToList();
                foreach (var episode in orderedEpisodes) {
                    if (_lookup.TryGetEpisodeIdsFor(episode, out var episodeIds))
                        if ((string.IsNullOrEmpty(episode.Path) || episode.IsVirtualItem) && (!knownEpisodeIds.Overlaps(episodeIds) || existingEpisodes.Overlaps(episodeIds)))
                            toRemoveEpisodes.Add(episode);
                        else
                            foreach (var episodeId in episodeIds)
                                existingEpisodes.Add(episodeId);
                }

                // Remove unknown or unwanted episodes.
                foreach (var episode in toRemoveEpisodes) {
                    _logger.LogDebug("Removing Episode {EpisodeName} from Season {SeasonNumber} for Series {SeriesName} (MainSeason={MainSeasonId})", episode.Name, 0, series.Name, seasonId);
                    _libraryManager.DeleteItem(episode, new() { DeleteFileLocation = false });
                }

                // Add missing episodes.
                if (ShouldAddMetadata && options.MetadataRefreshMode != MetadataRefreshMode.ValidationOnly) {
                    foreach (var seasonInfo in showInfo.SeasonList) {
                        foreach (var episodeId in await _apiManager.GetLocalEpisodeIdsForSeason(seasonInfo).ConfigureAwait(false))
                            existingEpisodes.Add(episodeId);

                        foreach (var episodeInfo in seasonInfo.SpecialsList) {
                            if (existingEpisodes.Contains(episodeInfo.Id))
                                continue;

                            if (CustomEpisodeProvider.AddVirtualEpisode(_libraryManager, _logger, showInfo, seasonInfo, episodeInfo, zeroSeason, series))
                                itemUpdated |= ItemUpdateType.MetadataImport;
                        }
                    }
                }

                // Merge versions.
                if (Plugin.Instance.Configuration.AutoMergeVersions && !_libraryManager.IsScanRunning && options.MetadataRefreshMode != MetadataRefreshMode.ValidationOnly) {
                    foreach (var episodeId in existingEpisodes) {
                        await _mergeVersionsManager.SplitAndMergeEpisodesByEpisodeId(episodeId).ConfigureAwait(false);
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
                    _logger.LogWarning("Unable to find season info for Season {SeasonNumber}. (MainSeason={MainSeasonId})", seasonNumber, showInfo.Id);
                    continue;
                }

                // Get known episodes, existing episodes, and episodes to remove.
                var episodeList = Math.Abs(seasonNumber - baseSeasonNumber) == 0 ? seasonInfo.EpisodeList : seasonInfo.AlternateEpisodesList;
                var knownEpisodeIds = ShouldAddMetadata ? episodeList.Select(episodeInfo => episodeInfo.Id).ToHashSet() : [];
                var existingEpisodes = new HashSet<string>();
                var toRemoveEpisodes = new List<Episode>();
                var orderedEpisodes = season.Children.OfType<Episode>().OrderBy(e => e.IndexNumber).ThenBy(e => e.IndexNumberEnd).ThenByDescending(e => e.IsVirtualItem).ToList();
                foreach (var episode in orderedEpisodes) {
                    if (_lookup.TryGetEpisodeIdsFor(episode, out var episodeIds)) {
                        if ((string.IsNullOrEmpty(episode.Path) || episode.IsVirtualItem) && (!knownEpisodeIds.Overlaps(episodeIds) || existingEpisodes.Overlaps(episodeIds)))
                            toRemoveEpisodes.Add(episode);
                        else
                            foreach (var episodeId in episodeIds)
                                existingEpisodes.Add(episodeId);
                    }
                }

                // Remove unknown or unwanted episodes.
                foreach (var episode in toRemoveEpisodes) {
                    _logger.LogDebug("Removing Episode {EpisodeName} from Season {SeasonNumber} for Series {SeriesName} (MainSeason={MainSeasonId})", episode.Name, seasonNumber, series.Name, seasonId);
                    _libraryManager.DeleteItem(episode, new() { DeleteFileLocation = false });
                }

                // Add missing episodes.
                if (ShouldAddMetadata && options.MetadataRefreshMode != MetadataRefreshMode.ValidationOnly) {
                    foreach (var episodeId in await _apiManager.GetLocalEpisodeIdsForSeason(seasonInfo).ConfigureAwait(false))
                        existingEpisodes.Add(episodeId);

                    foreach (var episodeInfo in episodeList) {
                        var episodeParentIndex = Ordering.GetSeasonNumber(showInfo, seasonInfo, episodeInfo);
                        if (episodeParentIndex != seasonNumber)
                            continue;

                        if (existingEpisodes.Contains(episodeInfo.Id))
                            continue;

                        if (CustomEpisodeProvider.AddVirtualEpisode(_libraryManager, _logger, showInfo, seasonInfo, episodeInfo, season, series))
                            itemUpdated |= ItemUpdateType.MetadataImport;
                    }
                }

                // Merge versions.
                if (Plugin.Instance.Configuration.AutoMergeVersions && !_libraryManager.IsScanRunning && options.MetadataRefreshMode != MetadataRefreshMode.ValidationOnly) {
                    foreach (var episodeId in existingEpisodes) {
                        await _mergeVersionsManager.SplitAndMergeEpisodesByEpisodeId(episodeId).ConfigureAwait(false);
                    }
                }
            }

            return itemUpdated;
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    private IEnumerable<(int, Season)> CreateMissingSeasons(Info.ShowInfo showInfo, Series series, Dictionary<int, Season> seasons) {
        foreach (var (seasonNumber, seasonInfo) in showInfo.SeasonOrderDictionary) {
            if (seasons.ContainsKey(seasonNumber))
                continue;
            var offset = seasonNumber - showInfo.GetBaseSeasonNumberForSeasonInfo(seasonInfo);
            var season = CustomSeasonProvider.AddVirtualSeason(_libraryManager, _logger, seasonInfo, offset, seasonNumber, series);
            if (season == null)
                continue;
            yield return (seasonNumber, season);
        }

        if (showInfo.HasSpecials && !seasons.ContainsKey(0)) {
            var season = CustomSeasonProvider.AddVirtualSeasonZero(_libraryManager, _logger, series);
            if (season != null)
                yield return (0, season);
        }
    }
}