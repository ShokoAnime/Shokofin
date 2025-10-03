using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Extensions;
using Shokofin.MergeVersions;

using Info = Shokofin.API.Info;

namespace Shokofin.Providers;
#pragma warning disable IDE0059
#pragma warning disable IDE0290

/// <summary>
/// The custom season provider. Responsible for de-duplicating seasons,
/// adding/removing "missing" episodes, and de-duplicating physical episodes.
/// </summary>
/// <remarks>
/// This needs to be it's own class because of internal Jellyfin shenanigans
/// about how a provider cannot also be a custom provider otherwise it won't
/// save the metadata.
/// </remarks>
public class CustomSeasonProvider(ILogger<CustomSeasonProvider> _logger, ShokoApiManager _apiManager, ShokoIdLookup _lookup, ILibraryManager _libraryManager, MergeVersionsManager _mergeVersionsManager) : IHasItemChangeMonitor, ICustomMetadataProvider<Season> {
    private static bool ShouldAddMetadata => Plugin.Instance.Configuration.AddMissingMetadata;

    public string Name => Plugin.MetadataProviderName;

    public bool HasChanged(BaseItem item, IDirectoryService directoryService) {
        // We're only interested in seasons.
        if (item is not Season season)
            return false;

        // We're not interested in the dummy season.
        if (!season.IndexNumber.HasValue)
            return false;

        // Silently abort if we're unable to get the shoko series id.
        var series = season.Series;
        if (!series.TryGetSeasonId(out var seasonId))
            return false;

        return true;
    }

    public async Task<ItemUpdateType> FetchAsync(Season season, MetadataRefreshOptions options, CancellationToken cancellationToken) {
        // We're not interested in the dummy season.
        if (!season.IndexNumber.HasValue)
            return ItemUpdateType.None;

        // Silently abort if we're unable to get the shoko series id.
        var series = season.Series;
        if (!_lookup.IsEnabledForItem(series) || !series.TryGetSeasonId(out var seasonId))
            return ItemUpdateType.None;

        var seasonNumber = season.IndexNumber!.Value;
        var trackerId = Plugin.Instance.Tracker.Add($"Providing custom info for Season \"{season.Name}\". (Path=\"{season.Path}\",MainSeason=\"{seasonId}\",Season={seasonNumber})");
        try {
            // Loudly abort if the show metadata doesn't exist.
            var showInfo = await _apiManager.GetShowInfoBySeasonId(seasonId).ConfigureAwait(false);
            if (showInfo == null || showInfo.SeasonList.Count == 0) {
                _logger.LogWarning("Unable to find show info for season. (MainSeason={MainSeasonId})", seasonId);
                return ItemUpdateType.None;
            }

            // Remove duplicates of the same season.
            var itemUpdated = ItemUpdateType.None;
            if (RemoveDuplicates(_libraryManager, _logger, seasonNumber, season, series, seasonId))
                itemUpdated |= ItemUpdateType.MetadataEdit;

            // Special handling of specials (pun intended).
            if (seasonNumber == 0) {
                // Get known episodes, existing episodes, and episodes to remove.
                var knownEpisodeIds = ShouldAddMetadata
                    ? showInfo.SpecialsDict.Keys.ToHashSet()
                    : showInfo.SpecialsDict
                        .Where(pair => pair.Value)
                        .Select(pair => pair.Key)
                        .ToHashSet();
                var existingEpisodes = new HashSet<string>();
                var toRemoveEpisodes = new List<Episode>();
                var orderedEpisodes = season.Children.OfType<Episode>().OrderBy(e => e.IndexNumber).ThenBy(e => e.IndexNumberEnd).ThenByDescending(e => e.IsVirtualItem).ToList();
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
                    foreach (var sI in showInfo.SeasonList) {
                        foreach (var episodeId in await _apiManager.GetLocalEpisodeIdsForSeason(sI).ConfigureAwait(false))
                            existingEpisodes.Add(episodeId);

                        foreach (var episodeInfo in sI.SpecialsList) {
                            if (existingEpisodes.Contains(episodeInfo.Id))
                                continue;

                            if (CustomEpisodeProvider.AddVirtualEpisode(_libraryManager, _logger, showInfo, sI, episodeInfo, season, series))
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
            // Every other "season."
            else {
                // Loudly abort if the season metadata doesn't exist.
                var seasonInfo = showInfo.GetSeasonInfoBySeasonNumber(seasonNumber);
                if (seasonInfo == null || !showInfo.TryGetBaseSeasonNumberForSeasonInfo(seasonInfo, out var baseSeasonNumber)) {
                    _logger.LogWarning("Unable to find series info for Season {SeasonNumber} in group for series. (MainSeason={MainSeasonId})", seasonNumber, seasonId);
                    return ItemUpdateType.None;
                }

                // Get known episodes, existing episodes, and episodes to remove.
                var episodeList = Math.Abs(seasonNumber - baseSeasonNumber) == 0 ? seasonInfo.EpisodeList : seasonInfo.AlternateEpisodesList;
                var knownEpisodeIds = ShouldAddMetadata
                    ? episodeList.Select(episodeInfo => episodeInfo.Id).ToHashSet()
                    : [];
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

    private static bool RemoveDuplicates(ILibraryManager libraryManager, ILogger logger, int seasonNumber, Season season, Series series, string seasonId) {
        // Remove the virtual season that matches the season.
        var searchList = libraryManager
            .GetItemList(
                new() {
                    ParentId = season.ParentId,
                    IncludeItemTypes = [Jellyfin.Data.Enums.BaseItemKind.Season],
                    ExcludeItemIds = [season.Id],
                    IndexNumber = seasonNumber,
                    DtoOptions = new(true),
                },
                true
            )
            .Where(item => !item.IndexNumber.HasValue)
            .ToList();
        if (searchList.Count > 0) {
            logger.LogDebug("Removing {Count} duplicates of Season {SeasonNumber} from Series {SeriesName} (MainSeason={MainSeasonId})", searchList.Count, seasonNumber, series.Name, seasonId);

            var deleteOptions = new DeleteOptions { DeleteFileLocation = false };
            foreach (var item in searchList)
                libraryManager.DeleteItem(item, deleteOptions);

            return true;
        }
        return false;
    }

    private static bool SeasonExists(ILibraryManager libraryManager, ILogger logger, string seriesPresentationUniqueKey, string seriesName, int seasonNumber) {
        var searchList = libraryManager.GetItemList(
            new() {
                IncludeItemTypes = [Jellyfin.Data.Enums.BaseItemKind.Season],
                IndexNumber = seasonNumber,
                GroupByPresentationUniqueKey = false,
                GroupBySeriesPresentationUniqueKey = true,
                SeriesPresentationUniqueKey = seriesPresentationUniqueKey,
                DtoOptions = new(true),
            },
            true
        );

        if (searchList.Count > 0) {
            logger.LogTrace("Season {SeasonNumber} for Series {SeriesName} exists.", seasonNumber, seriesName);
            return true;
        }

        return false;
    }

    public static Season? AddVirtualSeasonZero(ILibraryManager libraryManager, ILogger logger, Series series) {
        if (SeasonExists(libraryManager, logger, series.GetPresentationUniqueKey(), series.Name, 0))
            return null;

        var seasonName = libraryManager.GetLibraryOptions(series).SeasonZeroDisplayName;
        var season = new Season {
            Name = seasonName,
            IndexNumber = 0,
            SortName = $"AA - {seasonName}",
            ForcedSortName = $"AA - {seasonName}",
            Id = libraryManager.GetNewItemId(series.Id + "Season 0", typeof(Season)),
            IsVirtualItem = true,
            SeriesId = series.Id,
            SeriesName = series.Name,
            SeriesPresentationUniqueKey = series.GetPresentationUniqueKey(),
            DateCreated = series.DateCreated,
            DateModified = series.DateModified,
            DateLastSaved = series.DateLastSaved,
        };

        logger.LogInformation("Adding virtual Season {SeasonNumber} to Series {SeriesName}.", 0, series.Name);

        series.AddChild(season);

        return season;
    }

    public static Season? AddVirtualSeason(ILibraryManager libraryManager, ILogger logger, Info.SeasonInfo seasonInfo, int offset, int seasonNumber, Series series) {
        if (SeasonExists(libraryManager, logger, series.GetPresentationUniqueKey(), series.Name, seasonNumber))
            return null;

        var seasonId = libraryManager.GetNewItemId(series.Id + "Season " + seasonNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), typeof(Season));
        var season = SeasonProvider.CreateMetadata(seasonInfo, seasonNumber, offset, series, seasonId);

        logger.LogInformation("Adding virtual Season {SeasonNumber} to Series {SeriesName}. (Season={SeasonId},ExtraSeasons={ExtraIds})", seasonNumber, series.Name, seasonInfo.Id, seasonInfo.ExtraIds);

        series.AddChild(season);

        return season;
    }
}