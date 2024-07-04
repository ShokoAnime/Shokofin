using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.ExternalIds;

using Info = Shokofin.API.Info;

namespace Shokofin.Providers;

/// <summary>
/// The custom episode provider. Responsible for de-duplicating episodes.
/// </summary>
/// <remarks>
/// This needs to be it's own class because of internal Jellyfin shenanigans
/// about how a provider cannot also be a custom provider otherwise it won't
/// save the metadata.
/// </remarks>
public class CustomEpisodeProvider : ICustomMetadataProvider<Episode>
{
    public string Name => Plugin.MetadataProviderName;

    private readonly ILogger<CustomEpisodeProvider> Logger;

    private readonly IIdLookup Lookup;

    private readonly ILibraryManager LibraryManager;

    public CustomEpisodeProvider(ILogger<CustomEpisodeProvider> logger, IIdLookup lookup, ILibraryManager libraryManager)
    {
        Logger = logger;
        Lookup = lookup;
        LibraryManager = libraryManager;
    }

    public Task<ItemUpdateType> FetchAsync(Episode episode, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        var series = episode.Series;
        if (series is null)
            return Task.FromResult(ItemUpdateType.None);

        // Abort if we're unable to get the shoko episode id
        if (episode.ProviderIds.TryGetValue(ShokoEpisodeId.Name, out var episodeId))
            using (Plugin.Instance.Tracker.Enter($"Providing custom info for Episode \"{episode.Name}\". (Path=\"{episode.Path}\",IsMissingEpisode={episode.IsMissingEpisode})"))
                if (RemoveDuplicates(LibraryManager, Logger, episodeId, episode, series.GetPresentationUniqueKey()))
                    return Task.FromResult(ItemUpdateType.MetadataEdit);

        return Task.FromResult(ItemUpdateType.None);
    }

    public static bool RemoveDuplicates(ILibraryManager libraryManager, ILogger logger, string episodeId, Episode episode, string seriesPresentationUniqueKey)
    {
        // Remove any extra virtual episodes that matches the newly refreshed episode.
        var searchList = libraryManager.GetItemList(
            new() {
                ExcludeItemIds = new[] { episode.Id },
                HasAnyProviderId = new() { { ShokoEpisodeId.Name, episodeId } },
                IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Episode },
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

    private static bool EpisodeExists(ILibraryManager libraryManager, ILogger logger, string seriesPresentationUniqueKey, string episodeId, string seriesId, string? groupId)
    {
        var searchList = libraryManager.GetItemList(
            new() {
                IncludeItemTypes = new [] { Jellyfin.Data.Enums.BaseItemKind.Episode },
                HasAnyProviderId = new() { { ShokoEpisodeId.Name, episodeId } },
                GroupByPresentationUniqueKey = false,
                GroupBySeriesPresentationUniqueKey = true,
                SeriesPresentationUniqueKey = seriesPresentationUniqueKey,
                DtoOptions = new(true),
            },
            true
        );
        if (searchList.Count > 0) {
            logger.LogTrace("A virtual or physical episode entry already exists for Episode {EpisodeName}. Ignoring. (Episode={EpisodeId},Series={SeriesId},Group={GroupId})", searchList[0].Name, episodeId, seriesId, groupId);
            return true;
        }
        return false;
    }

    public static bool AddVirtualEpisode(ILibraryManager libraryManager, ILogger logger, Info.ShowInfo showInfo, Info.SeasonInfo seasonInfo, Info.EpisodeInfo episodeInfo, Season season, Series series)
    {
        if (EpisodeExists(libraryManager, logger, series.GetPresentationUniqueKey(), episodeInfo.Id, seasonInfo.Id, showInfo.GroupId))
            return false;

        var episodeId = libraryManager.GetNewItemId(season.Series.Id + " Season " + seasonInfo.Id + " Episode " + episodeInfo.Id, typeof(Episode));
        var episode = EpisodeProvider.CreateMetadata(showInfo, seasonInfo, episodeInfo, season, episodeId);

        logger.LogInformation("Adding virtual Episode {EpisodeNumber} in Season {SeasonNumber} for Series {SeriesName}. (Episode={EpisodeId},Series={SeriesId},ExtraSeries={ExtraIds},Group={GroupId})", episode.IndexNumber, season.IndexNumber, showInfo.Name, episodeInfo.Id, seasonInfo.Id, seasonInfo.ExtraIds, showInfo.GroupId);

        season.AddChild(episode);

        return true;
    }
}
