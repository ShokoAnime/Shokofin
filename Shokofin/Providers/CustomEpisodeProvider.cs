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
using Shokofin.ExternalIds;
using Shokofin.MergeVersions;

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
public class CustomEpisodeProvider(ILogger<CustomEpisodeProvider> _logger, ILibraryManager _libraryManager, ShokoIdLookup _lookup, MergeVersionsManager _mergeVersionsManager) : IHasItemChangeMonitor, ICustomMetadataProvider<Episode> {
    public string Name => Plugin.MetadataProviderName;

    public bool HasChanged(BaseItem item, IDirectoryService directoryService) {
        // We're only interested in episodes.
        if (item is not Episode episode)
            return false;

        // Abort if we're unable to get the shoko episode id.
        if (!episode.TryGetProviderId(ProviderNames.ShokoEpisode, out var episodeId))
            return false;

        return true;
    }

    public async Task<ItemUpdateType> FetchAsync(Episode episode, MetadataRefreshOptions options, CancellationToken cancellationToken) {
        var series = episode.Series;
        if (series is null)
            return ItemUpdateType.None;

        var itemUpdated = ItemUpdateType.None;
        if (_lookup.IsEnabledForItem(episode) && _lookup.TryGetEpisodeIdsFor(episode, out var episodeIds)) {
            using (Plugin.Instance.Tracker.Enter($"Providing custom info for Episode \"{episode.Name}\". (Path=\"{episode.Path}\",IsMissingEpisode={episode.IsMissingEpisode})")) {
                foreach (var episodeId in episodeIds) {
                    if (RemoveDuplicates(_libraryManager, _logger, episodeId, episode, series.GetPresentationUniqueKey()))
                        itemUpdated |= ItemUpdateType.MetadataEdit;
                }
            }

            if (Plugin.Instance.Configuration.AutoMergeVersions && !_libraryManager.IsScanRunning && options.MetadataRefreshMode != MetadataRefreshMode.ValidationOnly) {
                foreach (var episodeId in episodeIds)
                    await _mergeVersionsManager.SplitAndMergeEpisodesByEpisodeId(episodeId).ConfigureAwait(false);
                itemUpdated |= ItemUpdateType.MetadataEdit;
            }
        }

        return itemUpdated;
    }

    public static bool RemoveDuplicates(ILibraryManager libraryManager, ILogger logger, string episodeId, Episode episode, string seriesPresentationUniqueKey) {
        // Remove any extra virtual episodes that matches the newly refreshed episode.
        var searchList = libraryManager.GetItemList(
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
            logger.LogDebug("Removing {Count} duplicate episodes for episode {EpisodeName}. (Episode={EpisodeId})", searchList.Count, episode.Name, episodeId);

            var deleteOptions = new DeleteOptions { DeleteFileLocation = false };
            foreach (var item in searchList)
                libraryManager.DeleteItem(item, deleteOptions);

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

