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
using Shokofin.Resolvers;

using Info = Shokofin.API.Info;

namespace Shokofin.Providers;
#pragma warning disable IDE0059

/// <summary>
/// The custom episode provider. Responsible for de-duplicating episodes, both
/// virtual and physical.
/// </summary>
/// <remarks>
/// This needs to be it's own class because of internal Jellyfin shenanigans
/// about how a provider cannot also be a custom provider otherwise it won't
/// save the metadata.
/// </remarks>
public class CustomEpisodeProvider(
    ILogger<CustomEpisodeProvider> _logger,
    VirtualFileSystemService _vfsService,
    ILibraryManager _libraryManager,
    ShokoIdLookup _lookup,
#if NET9_0_OR_GREATER
    ShokoApiManager _apiManager,
#endif
    MergeVersionsManager 
_mergeVersionsManager) : IHasItemChangeMonitor, ICustomMetadataProvider<Episode> {
    public string Name => Plugin.MetadataProviderName;

    public bool HasChanged(BaseItem item, IDirectoryService directoryService) {
        // We're only interested in episodes.
        if (item is not Episode episode)
            return false;

        // Abort if we're unable to get the shoko episode id.
        if (!_lookup.IsEnabledForItem(episode) || !episode.TryGetProviderId(ProviderNames.ShokoEpisode, out var episodeId))
            return false;

        using (Plugin.Instance.Tracker.Enter($"Checking for custom info for Episode \"{episode.Name}\". (Path=\"{episode.Path}\")")) {
            if (_vfsService.TryGetCurrentLibraryGenerationMode(episode.ContainingFolderPath, out var iterativeGeneration, out var wasGenerated) && iterativeGeneration && !wasGenerated) {
                _logger.LogTrace("Skipped episode during iterative generation. (Episode={EpisodeId})", episodeId);
                return false;
            }
        }

        return true;
    }

    public async Task<ItemUpdateType> FetchAsync(Episode episode, MetadataRefreshOptions options, CancellationToken cancellationToken) {
        var series = episode.Series;
        if (!_lookup.IsEnabledForItem(series) || !series.TryGetSeasonId(out var seasonId))
            return ItemUpdateType.None;

        var trackerId = Plugin.Instance.Tracker.Add($"Providing custom info for Episode \"{episode.Name}\". (Path=\"{episode.Path}\",IsMissingEpisode={episode.IsMissingEpisode})");
        try {
            if (_vfsService.TryGetCurrentLibraryGenerationMode(series.Path, out var iterativeGeneration, out var wasGenerated) && iterativeGeneration && !wasGenerated) {
                _logger.LogTrace("Skipped episode during iterative generation. (MainSeason={MainSeasonId},Season={SeasonNumber},Episode={EpisodeNumber})", seasonId, episode.ParentIndexNumber, episode.IndexNumber);
                return ItemUpdateType.None;
            }

            var updateType = (ItemUpdateType)0;
#if NET9_0_OR_GREATER
            // Since Jellyfin 10.11.1 onwards they've fixed it so the creation date for videos doesn't follow the symlink but instead follows the target location, so to match the older behavior to get the date to match the import date, we now make sure the creation date is set to the import date here.
            if (episode.TryGetFileAndSeriesId(out var fileId, out var seriesId, vfsOnly: true)) {
                if (await _apiManager.GetFileInfo(fileId, seriesId).ConfigureAwait(false) is { } fileInfo) {
                    var createdAt = fileInfo.Shoko.ImportedAt ?? fileInfo.Shoko.CreatedAt;
                    if (episode.DateCreated != createdAt) {
                        episode.DateCreated = createdAt;
                        updateType |= ItemUpdateType.MetadataImport;
                    }
                }
            }
#endif

            if (_lookup.TryGetEpisodeIdsFor(episode, out var episodeIds)) {
                foreach (var episodeId in episodeIds) {
                    RemoveVirtualEpisodes(episodeId, episode, series.GetPresentationUniqueKey());
                    if (Plugin.Instance.Configuration.AutoMergeVersions && !_libraryManager.IsScanRunning && options.MetadataRefreshMode != MetadataRefreshMode.ValidationOnly) {
                        _mergeVersionsManager.ScheduleSplitAndMergeEpisodesByEpisodeId(episodeId);
                    }
                }
            }

            return updateType is 0 ? ItemUpdateType.None : updateType;
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    private bool RemoveVirtualEpisodes(string episodeId, Episode episode, string seriesPresentationUniqueKey) {
        // Remove any extra virtual episodes that matches the newly refreshed episode.
        var searchList = _libraryManager.GetItemList(
            new() {
                ExcludeItemIds = [episode.Id],
                HasAnyProviderId = new() { { ProviderNames.ShokoEpisode, episodeId } },
                IncludeItemTypes = [Jellyfin.Data.Enums.BaseItemKind.Episode],
                GroupByPresentationUniqueKey = false,
                GroupBySeriesPresentationUniqueKey = true,
                SeriesPresentationUniqueKey = seriesPresentationUniqueKey,
                DtoOptions = new(true),
            },
            true
        )
            .Where(item => string.IsNullOrEmpty(item.Path))
            .ToList();
        if (searchList.Count > 0) {
            _logger.LogDebug("Removing {Count} duplicate episodes for episode {EpisodeName}. (Episode={EpisodeId})", searchList.Count, episode.Name, episodeId);

            var deleteOptions = new DeleteOptions { DeleteFileLocation = false };
            foreach (var item in searchList)
                _libraryManager.DeleteItem(item, deleteOptions);

            return true;
        }

        return false;
    }

    private static bool EpisodeExists(ILibraryManager libraryManager, ILogger logger, string seriesPresentationUniqueKey, string episodeId, string seasonId) {
        var searchList = libraryManager.GetItemList(
            new() {
                IncludeItemTypes = [Jellyfin.Data.Enums.BaseItemKind.Episode],
                HasAnyProviderId = new() { { ProviderNames.ShokoEpisode, episodeId } },
                GroupByPresentationUniqueKey = false,
                GroupBySeriesPresentationUniqueKey = true,
                SeriesPresentationUniqueKey = seriesPresentationUniqueKey,
                DtoOptions = new(true),
            },
            true
        );
        if (searchList.Count > 0) {
            logger.LogTrace("A virtual or physical episode entry already exists for Episode {EpisodeName}. Ignoring. (Episode={EpisodeId},Season={SeasonId})", searchList[0].Name, episodeId, seasonId);
            return true;
        }
        return false;
    }

    public static bool AddVirtualEpisode(ILibraryManager libraryManager, ILogger logger, Info.ShowInfo showInfo, Info.SeasonInfo seasonInfo, Info.EpisodeInfo episodeInfo, Season season, Series series) {
        if (EpisodeExists(libraryManager, logger, series.GetPresentationUniqueKey(), episodeInfo.Id, seasonInfo.Id))
            return false;

        var episodeId = libraryManager.GetNewItemId(season.Series.Id + " Season " + seasonInfo.Id + " Episode " + episodeInfo.Id, typeof(Episode));
        var episode = EpisodeProvider.CreateMetadata(showInfo, seasonInfo, episodeInfo, season, episodeId);

        logger.LogInformation("Adding virtual Episode {EpisodeNumber} in Season {SeasonNumber} for Series {SeriesName}. (Episode={EpisodeId},Season={SeasonId},ExtraSeasons={ExtraIds})", episode.IndexNumber, season.IndexNumber, showInfo.Title, episodeInfo.Id, seasonInfo.Id, seasonInfo.ExtraIds);

        season.AddChild(episode);

        return true;
    }
}

