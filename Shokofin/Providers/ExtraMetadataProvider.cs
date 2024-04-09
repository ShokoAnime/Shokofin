using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Globalization;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.ExternalIds;
using Shokofin.Utils;

using Info = Shokofin.API.Info;

namespace Shokofin.Providers;

public class ExtraMetadataProvider : ICustomMetadataProvider<Series>, ICustomMetadataProvider<Season>, ICustomMetadataProvider<Episode>
{
    private readonly ShokoAPIManager ApiManager;

    private readonly IIdLookup Lookup;

    private readonly ILibraryManager LibraryManager;

    private readonly ILocalizationManager LocalizationManager;

    private readonly ILogger<ExtraMetadataProvider> Logger;

    string IMetadataProvider.Name => Plugin.MetadataProviderName;

    public ExtraMetadataProvider(ShokoAPIManager apiManager, IIdLookup lookUp, ILibraryManager libraryManager, ILocalizationManager localizationManager, ILogger<ExtraMetadataProvider> logger)
    {
        ApiManager = apiManager;
        Lookup = lookUp;
        LibraryManager = libraryManager;
        LocalizationManager = localizationManager;
        Logger = logger;
    }

    #region Series

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
        if (Plugin.Instance.Configuration.AddMissingMetadata) {
            var hasSpecials = false;
            var (seasons, _) = GetExistingSeasonsAndEpisodeIds(series);
            foreach (var pair in showInfo.SeasonOrderDictionary) {
                if (seasons.ContainsKey(pair.Key))
                    continue;
                if (pair.Value.SpecialsList.Count > 0)
                    hasSpecials = true;
                var offset = pair.Key - showInfo.SeasonNumberBaseDictionary[pair.Value.Id];
                var season = AddVirtualSeason(pair.Value, offset, pair.Key, series);
                if (season != null)
                    itemUpdated |= ItemUpdateType.MetadataImport;
            }

            if (hasSpecials && !seasons.ContainsKey(0)) {
                var season = AddVirtualSeason(0, series);
                if (season != null)
                    itemUpdated |= ItemUpdateType.MetadataImport;
            }
        }

        return itemUpdated;
    }
    private (Dictionary<int, Season>, HashSet<string>) GetExistingSeasonsAndEpisodeIds(Series series)
    {
        var seasons = new Dictionary<int, Season>();
        var episodes = new HashSet<string>();
        foreach (var item in series.GetRecursiveChildren()) switch (item) {
            case Season season:
                if (season.IndexNumber.HasValue)
                    seasons.TryAdd(season.IndexNumber.Value, season);
                // Add all known episode ids for the season.
                if (Lookup.TryGetSeriesIdFor(season, out var seriesId))
                    foreach (var episodeId in ApiManager.GetLocalEpisodeIdsForSeries(seriesId))
                        episodes.Add(episodeId);
                break;
            case Episode episode:
                // Get a hash-set of existing episodes – both physical and virtual – to exclude when adding new virtual episodes.
                if (Lookup.TryGetEpisodeIdsFor(episode, out var episodeIds))
                    foreach (var episodeId in episodeIds)
                        episodes.Add(episodeId);
                else if (Lookup.TryGetEpisodeIdFor(episode, out var episodeId))
                    episodes.Add(episodeId);
                break;
        }
        return (seasons, episodes);
    }

    private bool SeasonExists(string seriesPresentationUniqueKey, string seriesName, int seasonNumber)
    {
        var searchList = LibraryManager.GetItemList(new InternalItemsQuery {
            IncludeItemTypes = new [] { Jellyfin.Data.Enums.BaseItemKind.Season },
            IndexNumber = seasonNumber,
            SeriesPresentationUniqueKey = seriesPresentationUniqueKey,
            DtoOptions = new DtoOptions(true),
        }, true);

        if (searchList.Count > 0) {
            Logger.LogDebug("Season {SeasonName} for Series {SeriesName} was created in another concurrent thread, skipping.", searchList[0].Name, seriesName);
            return true;
        }

        return false;
    }

    private Season? AddVirtualSeason(int seasonNumber, Series series)
    {
        if (SeasonExists(series.GetPresentationUniqueKey(), series.Name, seasonNumber))
            return null;

        string seasonName;
        if (seasonNumber == 0)
            seasonName = LibraryManager.GetLibraryOptions(series).SeasonZeroDisplayName;
        else
            seasonName = string.Format(LocalizationManager.GetLocalizedString("NameSeasonNumber"), seasonNumber.ToString(CultureInfo.InvariantCulture));

        var season = new Season {
            Name = seasonName,
            IndexNumber = seasonNumber,
            SortName = seasonName,
            ForcedSortName = seasonName,
            Id = LibraryManager.GetNewItemId(
                series.Id + "Season " + seasonNumber.ToString(CultureInfo.InvariantCulture),
                typeof(Season)),
            IsVirtualItem = true,
            SeriesId = series.Id,
            SeriesName = series.Name,
            SeriesPresentationUniqueKey = series.GetPresentationUniqueKey(),
            DateModified = DateTime.UtcNow,
            DateLastSaved = DateTime.UtcNow,
        };

        Logger.LogInformation("Adding virtual Season {SeasonNumber:00} to Series {SeriesName}.", seasonNumber, series.Name);

        series.AddChild(season);

        return season;
    }

    private Season? AddVirtualSeason(Info.SeasonInfo seasonInfo, int offset, int seasonNumber, Series series)
    {
        if (SeasonExists(series.GetPresentationUniqueKey(), series.Name, seasonNumber))
            return null;

        var seasonId = LibraryManager.GetNewItemId(series.Id + "Season " + seasonNumber.ToString(CultureInfo.InvariantCulture), typeof(Season));
        var season = SeasonProvider.CreateMetadata(seasonInfo, seasonNumber, offset, series, seasonId);

        Logger.LogInformation("Adding virtual Season {SeasonNumber:00} to Series {SeriesName}. (Series={SeriesId})", seasonNumber, series.Name, seasonInfo.Id);

        series.AddChild(season);

        return season;
    }

    #endregion

    #region Season

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
        if (Plugin.Instance.Configuration.AddMissingMetadata) {
            // Get a hash-set of existing episodes – both physical and virtual – to exclude when adding new virtual episodes.
            var existingEpisodes = new HashSet<string>();
            foreach (var episode in season.Children.OfType<Episode>()) {
                if (Lookup.TryGetEpisodeIdsFor(episode, out var episodeIds))
                    foreach (var episodeId in episodeIds)
                        existingEpisodes.Add(episodeId);
                else if (Lookup.TryGetEpisodeIdFor(episode, out var episodeId))
                    existingEpisodes.Add(episodeId);
            }

            // Special handling of specials (pun intended).
            if (seasonNumber == 0) {
                foreach (var sI in showInfo.SeasonList) {
                    foreach (var episodeId in ApiManager.GetLocalEpisodeIdsForSeries(sI.Id))
                        existingEpisodes.Add(episodeId);

                    foreach (var episodeInfo in sI.SpecialsList) {
                        if (existingEpisodes.Contains(episodeInfo.Id))
                            continue;

                        if (AddVirtualEpisode(showInfo, sI, episodeInfo, season))
                            itemUpdated |= ItemUpdateType.MetadataImport;
                    }
                }
            }
            // Every other "season".
            else {
                var seasonInfo = showInfo.GetSeriesInfoBySeasonNumber(seasonNumber);
                if (seasonInfo == null) {
                    Logger.LogWarning("Unable to find series info for Season {SeasonNumber:00} in group for series. (Group={GroupId})", seasonNumber, showInfo.GroupId);
                    return ItemUpdateType.None;
                }

                var offset = seasonNumber - showInfo.SeasonNumberBaseDictionary[seasonInfo.Id];
                foreach (var episodeId in ApiManager.GetLocalEpisodeIdsForSeries(seasonInfo.Id))
                    existingEpisodes.Add(episodeId);

                foreach (var episodeInfo in seasonInfo.EpisodeList.Concat(seasonInfo.AlternateEpisodesList).Concat(seasonInfo.OthersList)) {
                    var episodeParentIndex = episodeInfo.IsSpecial ? 0 : Ordering.GetSeasonNumber(showInfo, seasonInfo, episodeInfo);
                    if (episodeParentIndex != seasonNumber)
                        continue;

                    if (existingEpisodes.Contains(episodeInfo.Id))
                        continue;

                    if (AddVirtualEpisode(showInfo, seasonInfo, episodeInfo, season))
                        itemUpdated |= ItemUpdateType.MetadataImport;
                }
            }
        }

        // Remove the virtual season/episode that matches the newly updated item
        var searchList = LibraryManager
            .GetItemList(
                new() {
                    ParentId = season.ParentId,
                    IncludeItemTypes = new [] { Jellyfin.Data.Enums.BaseItemKind.Season },
                    ExcludeItemIds = new [] { season.Id },
                    IndexNumber = seasonNumber,
                    DtoOptions = new DtoOptions(true),
                },
                true
            )
            .Where(item => !item.IndexNumber.HasValue)
            .ToList();
        if (searchList.Count > 0)
        {
            Logger.LogInformation("Removing {Count:00} duplicate seasons from Series {SeriesName} (Series={SeriesId})", searchList.Count, series.Name, seriesId);

            var deleteOptions = new DeleteOptions { DeleteFileLocation = false };
            foreach (var item in searchList)
                LibraryManager.DeleteItem(item, deleteOptions);

            itemUpdated |= ItemUpdateType.MetadataEdit;
        }


        return itemUpdated;
    }

    private bool EpisodeExists(string episodeId, string seriesId, string? groupId)
    {
        var searchList = LibraryManager.GetItemList(new InternalItemsQuery {
            IncludeItemTypes = new [] { Jellyfin.Data.Enums.BaseItemKind.Episode },
            HasAnyProviderId = new Dictionary<string, string> { [ShokoEpisodeId.Name] = episodeId },
            DtoOptions = new DtoOptions(true),
        }, true);

        if (searchList.Count > 0) {
            Logger.LogDebug("A virtual or physical episode entry already exists for Episode {EpisodeName}. Ignoring. (Episode={EpisodeId},Series={SeriesId},Group={GroupId})", searchList[0].Name, episodeId, seriesId, groupId);
            return true;
        }
        return false;
    }

    private bool AddVirtualEpisode(Info.ShowInfo showInfo, Info.SeasonInfo seasonInfo, Info.EpisodeInfo episodeInfo, Season season)
    {
        if (EpisodeExists(episodeInfo.Id, seasonInfo.Id, showInfo.GroupId))
            return false;

        var episodeId = LibraryManager.GetNewItemId(season.Series.Id + " Season " + seasonInfo.Id + " Episode " + episodeInfo.Id, typeof(Episode));
        var episode = EpisodeProvider.CreateMetadata(showInfo, seasonInfo, episodeInfo, season, episodeId);

        Logger.LogInformation("Adding virtual Episode {EpisodeNumber:000} in Season {SeasonNumber:00} for Series {SeriesName}. (Episode={EpisodeId},Series={SeriesId},Group={GroupId})", episode.IndexNumber, season.Name, showInfo.Name, episodeInfo.Id, seasonInfo.Id, showInfo.GroupId);

        season.AddChild(episode);

        return true;
    }

    #endregion

    #region Episode

    public Task<ItemUpdateType> FetchAsync(Episode episode, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        // Abort if we're unable to get the shoko episode id
        if (!Lookup.TryGetEpisodeIdFor(episode, out var episodeId))
            return Task.FromResult(ItemUpdateType.None);

        // Remove the virtual season/episode that matches the newly updated item
        var searchList = LibraryManager
            .GetItemList(
                new() {
                    ParentId = episode.ParentId,
                    IsVirtualItem = true,
                    ExcludeItemIds = new[] { episode.Id },
                    HasAnyProviderId = new() { { ShokoEpisodeId.Name, episodeId } },
                    IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Episode },
                    GroupByPresentationUniqueKey = false,
                    DtoOptions = new DtoOptions(true),
                },
                true
            );
        if (searchList.Count > 0) {
            Logger.LogInformation("Removing {Count:00} duplicate episodes for episode {EpisodeName}. (Episode={EpisodeId})", searchList.Count, episode.Name, episodeId);

            var deleteOptions = new DeleteOptions { DeleteFileLocation = false };
            foreach (var item in searchList)
                LibraryManager.DeleteItem(item, deleteOptions);

            return Task.FromResult(ItemUpdateType.MetadataEdit);
        }

        return Task.FromResult(ItemUpdateType.None);
    }

    #endregion
}
