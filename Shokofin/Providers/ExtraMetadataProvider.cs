using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Globalization;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Utils;

using Info = Shokofin.API.Info;

namespace Shokofin.Providers
{
    public class ExtraMetadataProvider : IServerEntryPoint
    {
        private readonly ShokoAPIManager ApiManager;

        private readonly IIdLookup Lookup;

        private readonly ILibraryManager LibraryManager;

        private readonly ILocalizationManager LocalizationManager;

        private readonly ILogger<ExtraMetadataProvider> Logger;

        public ExtraMetadataProvider(ShokoAPIManager apiManager, IIdLookup lookUp, ILibraryManager libraryManager, ILocalizationManager localizationManager, ILogger<ExtraMetadataProvider> logger)
        {
            ApiManager = apiManager;
            Lookup = lookUp;
            LibraryManager = libraryManager;
            LocalizationManager = localizationManager;
            Logger = logger;
        }

        public Task RunAsync()
        {
            LibraryManager.ItemAdded += OnLibraryManagerItemAdded;
            LibraryManager.ItemUpdated += OnLibraryManagerItemUpdated;
            LibraryManager.ItemRemoved += OnLibraryManagerItemRemoved;

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            LibraryManager.ItemAdded -= OnLibraryManagerItemAdded;
            LibraryManager.ItemUpdated -= OnLibraryManagerItemUpdated;
            LibraryManager.ItemRemoved -= OnLibraryManagerItemRemoved;
        }

        #region Locking

        private readonly ConcurrentDictionary<string, HashSet<string>> LockedIdDictionary = new();

        public bool TryLockActionForIdOFType(string type, string id, string action)
        {
            var key = $"{type}:{id}";
            if (!LockedIdDictionary.TryGetValue(key, out var hashSet)) {
                LockedIdDictionary.TryAdd(key, new HashSet<string>());
                if (!LockedIdDictionary.TryGetValue(key, out hashSet))
                    throw new Exception("Unable to set hash set");
            }
            return hashSet.Add(action);
        }

        public bool TryUnlockActionForIdOFType(string type, string id, string action)
        {
            var key = $"{type}:{id}";
            if (LockedIdDictionary.TryGetValue(key, out var hashSet))
                return hashSet.Remove(action);
            return false;
        }

        public bool IsActionForIdOfTypeLocked(string type, string id, string action)
        {
            var key = $"{type}:{id}";
            if (LockedIdDictionary.TryGetValue(key, out var hashSet))
                return hashSet.Contains(action);
            return false;
        }

        #endregion

        private void OnLibraryManagerItemAdded(object sender, ItemChangeEventArgs e)
        {
            if (e == null || e.Item == null || e.Parent == null || e.UpdateReason.HasFlag(ItemUpdateType.None))
                return;

            switch (e.Item) {
                case Series series: {
                    // Abort if we're unable to get the shoko series id
                    if (!Lookup.TryGetSeriesIdFor(series, out var seriesId))
                        return;

                    if (!TryLockActionForIdOFType("series", seriesId, "update"))
                        return;

                    try {
                        UpdateSeries(series, seriesId);
                    }
                    finally {
                        TryUnlockActionForIdOFType("series", seriesId, "update");
                    }

                    return;
                }
                case Season season: {
                    // We're not interested in the dummy season.
                    if (!season.IndexNumber.HasValue)
                        return;

                    if (e.Parent is not Series series)
                        return;

                    // Abort if we're unable to get the shoko series id
                    if (!Lookup.TryGetSeriesIdFor(season.Series, out var seriesId))
                        return;

                    if (IsActionForIdOfTypeLocked("series", seriesId, "update"))
                        return;

                    var seasonId = $"{seriesId}:{season.IndexNumber.Value}";
                    if (!TryLockActionForIdOFType("season", seasonId, "update"))
                        return;

                    try {
                        UpdateSeason(season, series, seriesId);
                    }
                    finally {
                        TryUnlockActionForIdOFType("season", seasonId, "update");
                    }

                    return;
                }
                case Episode episode: {
                    // Abort if we're unable to get the shoko episode id
                    if (!(Lookup.TryGetEpisodeIdFor(episode, out var episodeId) && Lookup.TryGetSeriesIdFromEpisodeId(episodeId, out var seriesId)))
                        return;

                    if (IsActionForIdOfTypeLocked("series", seriesId, "update"))
                        return;

                    if (episode.ParentIndexNumber.HasValue) {
                        var seasonId = $"{seriesId}:{episode.ParentIndexNumber.Value}";
                        if (IsActionForIdOfTypeLocked("season", seasonId, "update"))
                            return;
                    }

                    if (!TryLockActionForIdOFType("episode", episodeId, "update"))
                        return;

                    try {
                        RemoveDuplicateEpisodes(episode, episodeId);
                    }
                    finally {
                        TryUnlockActionForIdOFType("episode", episodeId, "update");
                    }

                    return;
                }
            }
        }

        private void OnLibraryManagerItemUpdated(object sender, ItemChangeEventArgs e)
        {
            if (e == null || e.Item == null || e.Parent == null || e.UpdateReason.HasFlag(ItemUpdateType.None))
                return;

            switch (e.Item) {
                case Series series: {
                    // Abort if we're unable to get the shoko episode id
                    if (!Lookup.TryGetSeriesIdFor(series, out var seriesId))
                        return;

                    if (!TryLockActionForIdOFType("series", seriesId, "update"))
                        return;

                    try {
                        UpdateSeries(series, seriesId);

                        RemoveDuplicateSeasons(series, seriesId);
                    }
                    finally {
                        TryUnlockActionForIdOFType("series", seriesId, "update");
                    }

                    return;
                }
                case Season season: {
                    // We're not interested in the dummy season.
                    if (!season.IndexNumber.HasValue)
                        return;

                    // Abort if we're unable to get the shoko series id
                    if (!Lookup.TryGetSeriesIdFor(season.Series, out var seriesId))
                        return;

                    if (IsActionForIdOfTypeLocked("series", seriesId, "update"))
                        return;

                    var seasonId = $"{seriesId}:{season.IndexNumber.Value}";
                    if (!TryLockActionForIdOFType("season", seasonId, "update"))
                        return;

                    try {
                        var series = season.Series;
                        UpdateSeason(season, series, seriesId);

                        RemoveDuplicateSeasons(season, series, season.IndexNumber.Value, seriesId);
                    }
                    finally {
                        TryUnlockActionForIdOFType("season", seasonId, "update");
                    }

                    return;
                }
                case Episode episode: {
                    // Abort if we're unable to get the shoko episode id
                    if (!(Lookup.TryGetEpisodeIdFor(episode, out var episodeId) && Lookup.TryGetSeriesIdFromEpisodeId(episodeId, out var seriesId)))
                        return;

                    if (IsActionForIdOfTypeLocked("series", seriesId, "update"))
                        return;

                    if (episode.ParentIndexNumber.HasValue) {
                        var seasonId = $"{seriesId}:{episode.ParentIndexNumber.Value}";
                        if (IsActionForIdOfTypeLocked("season", seasonId, "update"))
                            return;
                    }

                    if (!TryLockActionForIdOFType("episode", episodeId, "update"))
                        return;

                    try {
                        RemoveDuplicateEpisodes(episode, episodeId);
                    }
                    finally {
                        TryUnlockActionForIdOFType("episode", episodeId, "update");
                    }

                    return;
                }
            }
        }

        private void OnLibraryManagerItemRemoved(object sender, ItemChangeEventArgs e)
        {
            if (e == null || e.Item == null || e.Parent == null)
                return;

            if (e.Item.IsVirtualItem)
                return;

            switch (e.Item) {
                // Clean up after removing a series.
                case Series series: {
                    if (!Lookup.TryGetSeriesIdFor(series, out var _))
                        return;

                    foreach (var season in series.Children.OfType<Season>())
                        OnLibraryManagerItemRemoved(this, new ItemChangeEventArgs { Item = season, Parent = series, UpdateReason = ItemUpdateType.None });

                    return;
                }
                // Create a new virtual season if the real one was deleted and clean up extras if the season was deleted.
                case Season season: {
                    // Abort if we're unable to get the shoko episode id
                    if (!(Lookup.TryGetSeriesIdFor(season.Series, out var seriesId) && (e.Parent is Series series)))
                        return;

                    if (season.IndexNumber.HasValue)
                        UpdateSeason(season, series, seriesId, true);

                    return;
                }
                // Similarly, create a new virtual episode if the real one was deleted.
                case Episode episode: {
                    if (!Lookup.TryGetEpisodeIdFor(episode, out var episodeId))
                        return;

                    RemoveDuplicateEpisodes(episode, episodeId);

                    UpdateEpisode(episode, episodeId);

                    return;
                }
            }
        }

        private void UpdateSeries(Series series, string seriesId)
        {
            // Provide metadata for a series using Shoko's Group feature
            var showInfo = ApiManager.GetShowInfoForSeries(seriesId)
                .GetAwaiter()
                .GetResult();
            if (showInfo == null || showInfo.SeasonList.Count == 0) {
                Logger.LogWarning("Unable to find show info for series. (Series={SeriesID})", seriesId);
                return;
            }

            // Get the existing seasons and episode ids
            var (seasons, episodeIds) = GetExistingSeasonsAndEpisodeIds(series);

            // Add missing seasons
            foreach (var (seasonNumber, season) in CreateMissingSeasons(showInfo, series, seasons))
                seasons.TryAdd(seasonNumber, season);

            // Handle specials when grouped.
            if (seasons.TryGetValue(0, out var zeroSeason)) {
                foreach (var seasonInfo in showInfo.SeasonList) {
                    foreach (var episodeId in ApiManager.GetLocalEpisodeIdsForSeries(seasonInfo.Id))
                        episodeIds.Add(episodeId);

                    foreach (var episodeInfo in seasonInfo.SpecialsList) {
                        if (episodeIds.Contains(episodeInfo.Id))
                            continue;

                        AddVirtualEpisode(showInfo, seasonInfo, episodeInfo, zeroSeason);
                    }
                }
            }

            // Add missing episodes
            foreach (var (seasonNumber, seasonInfo) in showInfo.SeasonOrderDictionary) {
                if (!seasons.TryGetValue(seasonNumber, out var season) || season == null)
                    continue;

                foreach (var episodeId in ApiManager.GetLocalEpisodeIdsForSeries(seasonInfo.Id))
                    episodeIds.Add(episodeId);

                foreach (var episodeInfo in seasonInfo.EpisodeList) {
                    if (episodeIds.Contains(episodeInfo.Id))
                        continue;

                    AddVirtualEpisode(showInfo, seasonInfo, episodeInfo, season);
                }
            }
        }

        private void UpdateSeason(Season season, Series series, string seriesId, bool deleted = false)
        {
            var seasonNumber = season.IndexNumber!.Value;
            var showInfo = ApiManager.GetShowInfoForSeries(seriesId)
                .GetAwaiter()
                .GetResult();
            if (showInfo == null || showInfo.SeasonList.Count == 0) {
                Logger.LogWarning("Unable to find show info for season. (Series={SeriesId})", seriesId);
                return;
            }

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
                if (deleted) 
                    season = AddVirtualSeason(0, series);

                foreach (var sI in showInfo.SeasonList) {
                    foreach (var episodeId in ApiManager.GetLocalEpisodeIdsForSeries(sI.Id))
                        existingEpisodes.Add(episodeId);

                    foreach (var episodeInfo in sI.SpecialsList) {
                        if (existingEpisodes.Contains(episodeInfo.Id))
                            continue;

                        AddVirtualEpisode(showInfo, sI, episodeInfo, season);
                    }
                }
            }
            // Every other "season".
            else {
                var seasonInfo = showInfo.GetSeriesInfoBySeasonNumber(seasonNumber);
                if (seasonInfo == null) {
                    Logger.LogWarning("Unable to find series info for Season {SeasonNumber:00} in group for series. (Group={GroupId})", seasonNumber, showInfo.GroupId);
                    return;
                }

                var offset = seasonNumber - showInfo.SeasonNumberBaseDictionary[seasonInfo.Id];
                if (deleted)
                    season = AddVirtualSeason(seasonInfo, offset, seasonNumber, series);

                foreach (var episodeId in ApiManager.GetLocalEpisodeIdsForSeries(seasonInfo.Id))
                    existingEpisodes.Add(episodeId);

                foreach (var episodeInfo in seasonInfo.EpisodeList.Concat(seasonInfo.AlternateEpisodesList).Concat(seasonInfo.OthersList)) {
                    var episodeParentIndex = episodeInfo.IsSpecial ? 0 : Ordering.GetSeasonNumber(showInfo, seasonInfo, episodeInfo);
                    if (episodeParentIndex != seasonNumber)
                        continue;

                    if (existingEpisodes.Contains(episodeInfo.Id))
                        continue;

                    AddVirtualEpisode(showInfo, seasonInfo, episodeInfo, season);
                }
            }
        }

        private void UpdateEpisode(Episode episode, string episodeId)
        {
            var showInfo = ApiManager.GetShowInfoForEpisode(episodeId)
                    .GetAwaiter()
                    .GetResult();
            if (showInfo == null || showInfo.SeasonList.Count == 0) {
                Logger.LogWarning("Unable to find show info for episode. (Episode={EpisodeId})", episode);
                return;
            }
            var seasonInfo = ApiManager.GetSeasonInfoForEpisode(episodeId)
                .GetAwaiter()
                .GetResult();
            var episodeInfo = seasonInfo.EpisodeList.FirstOrDefault(e => e.Id == episodeId);
            var episodeIds = ApiManager.GetLocalEpisodeIdsForSeries(seasonInfo.Id);
            if (!episodeIds.Contains(episodeId))
                AddVirtualEpisode(showInfo, seasonInfo, episodeInfo, episode.Season);
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

        #region Seasons

        private IEnumerable<(int, Season)> CreateMissingSeasons(Info.ShowInfo showInfo, Series series, Dictionary<int, Season> seasons)
        {
            bool hasSpecials = false;
            foreach (var pair in showInfo.SeasonOrderDictionary) {
                if (seasons.ContainsKey(pair.Key))
                    continue;
                if (pair.Value.SpecialsList.Count > 0)
                    hasSpecials = true;
                var offset = pair.Key - showInfo.SeasonNumberBaseDictionary[pair.Value.Id];
                var season = AddVirtualSeason(pair.Value, offset, pair.Key, series);
                if (season == null)
                    continue;
                yield return (pair.Key, season);
            }
            if (hasSpecials && !seasons.ContainsKey(0)) {
                var season = AddVirtualSeason(0, series);
                if (season != null)
                    yield return (0, season);
            }
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

        private Season AddVirtualSeason(int seasonNumber, Series series)
        {
            if (SeasonExists(series.GetPresentationUniqueKey(), series.Name, seasonNumber))
                return null;

            string seasonName;
            if (seasonNumber == 0)
                seasonName = LibraryManager.GetLibraryOptions(series).SeasonZeroDisplayName;
            else
                seasonName = string.Format(
                    LocalizationManager.GetLocalizedString("NameSeasonNumber"),
                    seasonNumber.ToString(CultureInfo.InvariantCulture));

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

        private Season AddVirtualSeason(Info.SeasonInfo seasonInfo, int offset, int seasonNumber, Series series)
        {
            if (SeasonExists(series.GetPresentationUniqueKey(), series.Name, seasonNumber))
                return null;

            var seasonId = LibraryManager.GetNewItemId(series.Id + "Season " + seasonNumber.ToString(CultureInfo.InvariantCulture), typeof(Season));
            var season = SeasonProvider.CreateMetadata(seasonInfo, seasonNumber, offset, series, seasonId);

            Logger.LogInformation("Adding virtual Season {SeasonNumber:00} to Series {SeriesName}. (Series={SeriesId})", seasonNumber, series.Name, seasonInfo.Id);

            series.AddChild(season);

            return season;
        }

        public void RemoveDuplicateSeasons(Series series, string seriesId)
        {
            var seasonNumbers = new HashSet<int>();
            var seasons = series
                .GetSeasons(null, new DtoOptions(true))
                .OfType<Season>()
                .OrderBy(s => s.IsVirtualItem);
            foreach (var season in seasons) {
                if (!season.IndexNumber.HasValue)
                    continue;

                var seasonNumber = season.IndexNumber.Value;
                if (!seasonNumbers.Add(seasonNumber))
                    continue;

                RemoveDuplicateSeasons(season, series, seasonNumber, seriesId);
            }
        }

        public void RemoveDuplicateSeasons(Season season, Series series, int seasonNumber, string seriesId)
        {
            var searchList = LibraryManager.GetItemList(new InternalItemsQuery {
                IncludeItemTypes = new [] { Jellyfin.Data.Enums.BaseItemKind.Season },
                ExcludeItemIds = new [] { season.Id },
                IndexNumber = seasonNumber,
                DtoOptions = new DtoOptions(true),
            }, true).Where(item => !item.IndexNumber.HasValue).ToList();

            if (searchList.Count == 0)
                return;

            Logger.LogWarning("Removing {Count:00} duplicate seasons from Series {SeriesName} (Series={SeriesId})", searchList.Count, series.Name, seriesId);
            var deleteOptions = new DeleteOptions {
                DeleteFileLocation = false,
            };
            foreach (var item in searchList)
                LibraryManager.DeleteItem(item, deleteOptions);

            var episodeNumbers = new HashSet<int?>();
            // Ordering by `IsVirtualItem` will put physical episodes first.
            foreach (var episode in season.GetEpisodes(null, new DtoOptions(true)).OfType<Episode>().OrderBy(e => e.IsVirtualItem)) {
                // Abort if we're unable to get the shoko episode id
                if (!Lookup.TryGetEpisodeIdFor(episode, out var episodeId))
                    continue;

                // Only iterate over the same index number once.
                if (!episodeNumbers.Add(episode.IndexNumber))
                    continue;

                RemoveDuplicateEpisodes(episode, episodeId);
            }
        }

        #endregion
        #region Episodes

        private bool EpisodeExists(string episodeId, string seriesId, string groupId)
        {
            var searchList = LibraryManager.GetItemList(new InternalItemsQuery {
                IncludeItemTypes = new [] { Jellyfin.Data.Enums.BaseItemKind.Episode },
                HasAnyProviderId = new Dictionary<string, string> { ["Shoko Episode"] = episodeId },
                DtoOptions = new DtoOptions(true),
            }, true);

            if (searchList.Count > 0) {
                Logger.LogDebug("A virtual or physical episode entry already exists for Episode {EpisodeName}. Ignoring. (Episode={EpisodeId},Series={SeriesId},Group={GroupId})", searchList[0].Name, episodeId, seriesId, groupId);
                return true;
            }
            return false;
        }

        private void AddVirtualEpisode(Info.ShowInfo showInfo, Info.SeasonInfo seasonInfo, Info.EpisodeInfo episodeInfo, Season season)
        {
            var groupId = showInfo?.GroupId ?? null;
            if (EpisodeExists(episodeInfo.Id, seasonInfo.Id, groupId))
                return;

            var episodeId = LibraryManager.GetNewItemId(season.Series.Id + "Season " + seasonInfo.Id + " Episode " + episodeInfo.Id, typeof(Episode));
            var episode = EpisodeProvider.CreateMetadata(showInfo, seasonInfo, episodeInfo, season, episodeId);

            Logger.LogInformation("Adding virtual Episode {EpisodeNumber:000} in Season {SeasonNumber:00} for Series {SeriesName}. (Episode={EpisodeId},Series={SeriesId},Group={GroupId})", episode.IndexNumber, season.Name, showInfo?.Name ?? seasonInfo.Shoko.Name, episodeInfo.Id, seasonInfo.Id, groupId);

            season.AddChild(episode);
        }

        private void RemoveDuplicateEpisodes(Episode episode, string episodeId)
        {
            var query = new InternalItemsQuery {
                IsVirtualItem = true,
                ExcludeItemIds = new [] { episode.Id },
                HasAnyProviderId = new Dictionary<string, string> { ["Shoko Episode"] = episodeId },
                IncludeItemTypes = new [] {Jellyfin.Data.Enums.BaseItemKind.Episode },
                GroupByPresentationUniqueKey = false,
                DtoOptions = new DtoOptions(true),
            };

            var existingVirtualItems = LibraryManager.GetItemList(query);

            var deleteOptions = new DeleteOptions {
                DeleteFileLocation = false,
            };

            // Remove the virtual season/episode that matches the newly updated item
            foreach (var item in existingVirtualItems)
                LibraryManager.DeleteItem(item, deleteOptions);

            if (existingVirtualItems.Count > 0)
                Logger.LogInformation("Removed {Count:00} duplicate episodes for episode {EpisodeName}. (Episode={EpisodeId})", existingVirtualItems.Count, episode.Name, episodeId);
        }

        #endregion
    }
}
