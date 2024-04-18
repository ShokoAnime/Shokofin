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

using Info = Shokofin.API.Info;

namespace Shokofin.Providers;

/// <summary>
/// The custom season provider. Responsible for de-duplicating seasons and
/// adding/removing "missing" episodes.
/// </summary>
/// <remarks>
/// This needs to be it's own class because of internal Jellyfin shenanigans
/// about how a provider cannot also be a custom provider otherwise it won't
/// save the metadata.
/// </remarks>
public class CustomSeasonProvider : ICustomMetadataProvider<Season>
{
    public string Name => Plugin.MetadataProviderName;

    private readonly ILogger<CustomSeasonProvider> Logger;

    private readonly ShokoAPIManager ApiManager;

    private readonly IIdLookup Lookup;

    private readonly ILibraryManager LibraryManager;

    public CustomSeasonProvider(ILogger<CustomSeasonProvider> logger, ShokoAPIManager apiManager, IIdLookup lookup, ILibraryManager libraryManager)
    {
        Logger = logger;
        ApiManager = apiManager;
        Lookup = lookup;
        LibraryManager = libraryManager;
    }

    public async Task<ItemUpdateType> FetchAsync(Season season, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        // We're not interested in the dummy season.
        if (!season.IndexNumber.HasValue)
            return ItemUpdateType.None;

        // Abort if we're unable to get the shoko series id
        var series = season.Series;
        if (!Lookup.TryGetSeriesIdFor(series, out var seriesId))
            return ItemUpdateType.None;

        var seasonNumber = season.IndexNumber!.Value;
        var showInfo = await ApiManager.GetShowInfoForSeries(seriesId);
        if (showInfo == null || showInfo.SeasonList.Count == 0) {
            Logger.LogWarning("Unable to find show info for season. (Series={SeriesId})", seriesId);
            return ItemUpdateType.None;
        }

        var itemUpdated = ItemUpdateType.None;
        if (Plugin.Instance.Configuration.AddMissingMetadata && options.MetadataRefreshMode != MetadataRefreshMode.ValidationOnly) {
            // Special handling of specials (pun intended).
            if (seasonNumber == 0) {
                var goodKnownEpisodeIds = showInfo.SpecialsSet;
                var toRemove = new List<Episode>();
                var existingEpisodes = new HashSet<string>();
                foreach (var episode in season.Children.OfType<Episode>()) {
                    if (Lookup.TryGetEpisodeIdsFor(episode, out var episodeIds))
                        if (episode.IsVirtualItem && !goodKnownEpisodeIds.Overlaps(episodeIds)) {
                            toRemove.Add(episode);
                        }
                        else {
                            foreach (var episodeId in episodeIds)
                                existingEpisodes.Add(episodeId);
                        }
                    else if (Lookup.TryGetEpisodeIdFor(episode, out var episodeId)) {
                        if (episode.IsVirtualItem && !goodKnownEpisodeIds.Contains(episodeId))
                            toRemove.Add(episode);
                        else
                            existingEpisodes.Add(episodeId);
                    }
                }

                foreach (var episode in toRemove) {
                    Logger.LogDebug("Removing unknown Episode {EpisodeName} from Season {SeasonNumber} for Series {SeriesName} (Series={SeriesId})", episode.Name, 0, series.Name, seriesId);
                    LibraryManager.DeleteItem(episode, new() { DeleteFileLocation = false });
                }

                foreach (var sI in showInfo.SeasonList) {
                    foreach (var episodeId in ApiManager.GetLocalEpisodeIdsForSeries(sI.Id))
                        existingEpisodes.Add(episodeId);

                    foreach (var episodeInfo in sI.SpecialsList) {
                        if (existingEpisodes.Contains(episodeInfo.Id))
                            continue;

                        if (CustomEpisodeProvider.AddVirtualEpisode(LibraryManager, Logger, showInfo, sI, episodeInfo, season, series))
                            itemUpdated |= ItemUpdateType.MetadataImport;
                    }
                }
            }
            // Every other "season".
            else {
                var seasonInfo = showInfo.GetSeasonInfoBySeasonNumber(seasonNumber);
                if (seasonInfo == null || !showInfo.TryGetBaseSeasonNumberForSeasonInfo(seasonInfo, out var baseSeasonNumber)) {
                    Logger.LogWarning("Unable to find series info for Season {SeasonNumber} in group for series. (Group={GroupId})", seasonNumber, showInfo.GroupId);
                    return ItemUpdateType.None;
                }
                var offset = Math.Abs(seasonNumber - baseSeasonNumber);

                var episodeList = offset == 0 ? seasonInfo.EpisodeList : seasonInfo.AlternateEpisodesList;
                var goodKnownEpisodeIds = episodeList
                    .Select(episodeInfo => episodeInfo.Id)
                    .ToHashSet();
                var toRemove = new List<Episode>();
                var existingEpisodes = new HashSet<string>();
                foreach (var episode in season.Children.OfType<Episode>()) {
                    if (Lookup.TryGetEpisodeIdsFor(episode, out var episodeIds))
                        if (episode.IsVirtualItem && !goodKnownEpisodeIds.Overlaps(episodeIds)) {
                            toRemove.Add(episode);
                        }
                        else {
                            foreach (var episodeId in episodeIds)
                                existingEpisodes.Add(episodeId);
                        }
                    else if (Lookup.TryGetEpisodeIdFor(episode, out var episodeId)) {
                        if (episode.IsVirtualItem && !goodKnownEpisodeIds.Contains(episodeId))
                            toRemove.Add(episode);
                        else
                            existingEpisodes.Add(episodeId);
                    }
                }

                foreach (var episode in toRemove) {
                    Logger.LogDebug("Removing unknown Episode {EpisodeName} from Season {SeasonNumber} for Series {SeriesName} (Series={SeriesId})", episode.Name, seasonNumber, series.Name, seriesId);
                    LibraryManager.DeleteItem(episode, new() { DeleteFileLocation = false });
                }

                foreach (var episodeId in ApiManager.GetLocalEpisodeIdsForSeries(seasonInfo.Id))
                    existingEpisodes.Add(episodeId);

                foreach (var episodeInfo in episodeList) {
                    if (existingEpisodes.Contains(episodeInfo.Id))
                        continue;

                    if (CustomEpisodeProvider.AddVirtualEpisode(LibraryManager, Logger, showInfo, seasonInfo, episodeInfo, season, series))
                        itemUpdated |= ItemUpdateType.MetadataImport;
                }
            }
        }

        if (RemoveDuplicates(LibraryManager, Logger, seasonNumber, season, series, seriesId))
            itemUpdated |= ItemUpdateType.MetadataEdit;

        return itemUpdated;
    }
    private static bool RemoveDuplicates(ILibraryManager libraryManager, ILogger logger, int seasonNumber, Season season, Series series, string seriesId)
    {
        // Remove the virtual season/episode that matches the newly updated item
        var searchList = libraryManager
            .GetItemList(
                new() {
                    ParentId = season.ParentId,
                    IncludeItemTypes = new [] { Jellyfin.Data.Enums.BaseItemKind.Season },
                    ExcludeItemIds = new [] { season.Id },
                    IndexNumber = seasonNumber,
                    DtoOptions = new(true),
                },
                true
            )
            .Where(item => !item.IndexNumber.HasValue)
            .ToList();
        if (searchList.Count > 0)
        {
            logger.LogDebug("Removing {Count:00} duplicates of Season {SeasonNumber:00} from Series {SeriesName} (Series={SeriesId})", searchList.Count, seasonNumber, series.Name, seriesId);

            var deleteOptions = new DeleteOptions { DeleteFileLocation = false };
            foreach (var item in searchList)
                libraryManager.DeleteItem(item, deleteOptions);

            return true;
        }
        return false;
    }

    private static bool SeasonExists(ILibraryManager libraryManager, ILogger logger, string seriesPresentationUniqueKey, string seriesName, int seasonNumber)
    {
        var searchList = libraryManager.GetItemList(
            new() {
                IncludeItemTypes = new [] { Jellyfin.Data.Enums.BaseItemKind.Season },
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

    public static Season? AddVirtualSeasonZero(ILibraryManager libraryManager, ILogger logger, Series series)
    {
        if (SeasonExists(libraryManager, logger, series.GetPresentationUniqueKey(), series.Name, 0))
            return null;

        var seasonName = libraryManager.GetLibraryOptions(series).SeasonZeroDisplayName;
        var season = new Season {
            Name = seasonName,
            IndexNumber = 0,
            SortName = seasonName,
            ForcedSortName = seasonName,
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

    public static Season? AddVirtualSeason(ILibraryManager libraryManager, ILogger logger, Info.SeasonInfo seasonInfo, int offset, int seasonNumber, Series series)
    {
        if (SeasonExists(libraryManager, logger, series.GetPresentationUniqueKey(), series.Name, seasonNumber))
            return null;

        var seasonId = libraryManager.GetNewItemId(series.Id + "Season " + seasonNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), typeof(Season));
        var season = SeasonProvider.CreateMetadata(seasonInfo, seasonNumber, offset, series, seasonId);

        logger.LogInformation("Adding virtual Season {SeasonNumber} to Series {SeriesName}. (Series={SeriesId})", seasonNumber, series.Name, seasonInfo.Id);

        series.AddChild(season);

        return season;
    }
}