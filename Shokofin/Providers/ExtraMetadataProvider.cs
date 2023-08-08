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
            if (e == null || e.Item == null || e.Parent == null || !(e.UpdateReason.HasFlag(ItemUpdateType.MetadataImport) || e.UpdateReason.HasFlag(ItemUpdateType.MetadataDownload)))
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
            if (e == null || e.Item == null || e.Parent == null || !(e.UpdateReason.HasFlag(ItemUpdateType.MetadataImport) || e.UpdateReason.HasFlag(ItemUpdateType.MetadataDownload)))
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
            if (e == null || e.Item == null || e.Parent == null || !(e.UpdateReason.HasFlag(ItemUpdateType.MetadataImport) || e.UpdateReason.HasFlag(ItemUpdateType.MetadataDownload)))
                return;

            if (e.Item.IsVirtualItem)
                return;

            switch (e.Item) {
                // Clean up after removing a series.
                case Series series: {
                    if (!Lookup.TryGetSeriesIdFor(series, out var seriesId))
                        return;

                    RemoveExtras(series, seriesId);

                    if (Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup) {
                        foreach (var season in series.Children.OfType<Season>()) {
                            OnLibraryManagerItemRemoved(this, new ItemChangeEventArgs { Item = season, Parent = series, UpdateReason = ItemUpdateType.None });
                        }
                    }

                    return;
                }
                // Create a new virtual season if the real one was deleted and clean up extras if the season was deleted.
                case Season season: {
                    // Abort if we're unable to get the shoko episode id
                    if (!(Lookup.TryGetSeriesIdFor(season.Series, out var seriesId) && (e.Parent is Series series)))
                        return;

                    if (e.UpdateReason == ItemUpdateType.None)
                        RemoveExtras(season, seriesId);
                    else
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
            if (Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup) {
                var showInfo = ApiManager.GetShowInfoForSeries(seriesId, Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default)
                    .GetAwaiter()
                    .GetResult();
                if (showInfo == null) {
                    Logger.LogWarning("Unable to find group info for series. (Series={SeriesID})", seriesId);
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
                foreach (var pair in showInfo.SeasonOrderDictionary) {
                    var seasonNumber= pair.Key;
                    if (!seasons.TryGetValue(seasonNumber, out var season) || season == null)
                        continue;

                    var seasonInfo = pair.Value;
                    foreach (var episodeId in ApiManager.GetLocalEpisodeIdsForSeries(seasonInfo.Id))
                        episodeIds.Add(episodeId);
                    foreach (var episodeInfo in seasonInfo.EpisodeList) {
                        if (episodeIds.Contains(episodeInfo.Id))
                            continue;

                        AddVirtualEpisode(showInfo, seasonInfo, episodeInfo, season);
                    }
                }

                // We add the extras to the season if we're using Shoko Groups.
                AddExtras(series, showInfo.DefaultSeason);

                foreach (var pair in showInfo.SeasonOrderDictionary) {
                    if (!seasons.TryGetValue(pair.Key, out var season) || season == null)
                        continue;

                    AddExtras(season, pair.Value);
                }
            }
            // Provide metadata for other series
            else {
                var seasonInfo = ApiManager.GetSeasonInfoForSeries(seriesId)
                    .GetAwaiter()
                    .GetResult();
                if (seasonInfo == null) {
                    Logger.LogWarning("Unable to find series info. (Series={SeriesID})", seriesId);
                    return;
                }

                // Get the existing seasons and episode ids
                var (seasons, episodeIds) = GetExistingSeasonsAndEpisodeIds(series);

                // Compute the season numbers for each episode in the series in advance, since we need to filter out the missing seasons
                var episodeInfoToSeasonNumberDirectory = seasonInfo.RawEpisodeList.ToDictionary(e => e, e => Ordering.GetSeasonNumber(null, seasonInfo, e));

                // Add missing seasons
                var allKnownSeasonNumbers = episodeInfoToSeasonNumberDirectory.Values.Distinct().ToList();
                foreach (var (seasonNumber, season) in CreateMissingSeasons(seasonInfo, series, seasons, allKnownSeasonNumbers))
                    seasons.Add(seasonNumber, season);

                // Add missing episodes
                foreach (var episodeId in ApiManager.GetLocalEpisodeIdsForSeries(seasonInfo.Id))
                    episodeIds.Add(episodeId);
                foreach (var episodeInfo in seasonInfo.RawEpisodeList) {
                    if (episodeInfo.ExtraType != null)
                        continue;

                    if (episodeIds.Contains(episodeInfo.Id))
                        continue;

                    var seasonNumber = episodeInfoToSeasonNumberDirectory[episodeInfo];
                    if (!seasons.TryGetValue(seasonNumber, out var season) || season == null)
                        continue;

                    AddVirtualEpisode(null, seasonInfo, episodeInfo, season);
                }

                // We add the extras to the series if not.
                AddExtras(series, seasonInfo);
            }
        }

        private void UpdateSeason(Season season, Series series, string seriesId, bool deleted = false)
        {
            var seasonNumber = season.IndexNumber!.Value;
            var seriesGrouping = Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup;
            Info.ShowInfo showInfo = null;
            Info.SeasonInfo seasonInfo = null;
            // Provide metadata for a season using Shoko's Group feature
            if (seriesGrouping) {
                showInfo = ApiManager.GetShowInfoForSeries(seriesId, Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default)
                    .GetAwaiter()
                    .GetResult();
                if (showInfo == null) {
                    Logger.LogWarning("Unable to find group info for series. (Series={SeriesId})", seriesId);
                    return;
                }

                if (seasonNumber == 0) {
                    if (deleted) {
                        season = AddVirtualSeason(0, series);
                    }
                }
                else {
                    seasonInfo = showInfo.GetSeriesInfoBySeasonNumber(seasonNumber);
                    if (seasonInfo == null) {
                        Logger.LogWarning("Unable to find series info for Season {SeasonNumber:00} in group for series. (Group={GroupId})", seasonNumber, showInfo.Id);
                        return;
                    }

                    if (deleted) {
                        var offset = seasonNumber - showInfo.SeasonNumberBaseDictionary[seasonInfo];
                        season = AddVirtualSeason(seasonInfo, offset, seasonNumber, series);
                    }
                }
            }
            // Provide metadata for other seasons
            else {
                seasonInfo = ApiManager.GetSeasonInfoForSeries(seriesId)
                    .GetAwaiter()
                    .GetResult();
                if (seasonInfo == null) {
                    Logger.LogWarning("Unable to find series info for Season {SeasonNumber:00}. (Series={SeriesId})", seasonNumber, seriesId);
                    return;
                }

                if (deleted) {
                    var mergeFriendly = Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.MergeFriendly && seasonInfo.TvDB != null;
                    season = seasonNumber == 1 && (!mergeFriendly) ? AddVirtualSeason(seasonInfo, 0, 1, series) : AddVirtualSeason(seasonNumber, series);
                }
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

            // Handle specials when grouped.
            if (seasonNumber == 0) {
                if (seriesGrouping) {
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
                else {
                    foreach (var episodeId in ApiManager.GetLocalEpisodeIdsForSeries(seasonInfo.Id))
                        existingEpisodes.Add(episodeId);
                    foreach (var episodeInfo in seasonInfo.SpecialsList) {
                        if (existingEpisodes.Contains(episodeInfo.Id))
                            continue;

                        AddVirtualEpisode(showInfo, seasonInfo, episodeInfo, season);
                    }
                }
            }
            else {
                foreach (var episodeId in ApiManager.GetLocalEpisodeIdsForSeries(seasonInfo.Id))
                    existingEpisodes.Add(episodeId);
                foreach (var episodeInfo in seasonInfo.EpisodeList) {
                    var episodeParentIndex = Ordering.GetSeasonNumber(showInfo, seasonInfo, episodeInfo);
                    if (episodeParentIndex != seasonNumber)
                        continue;

                    if (existingEpisodes.Contains(episodeInfo.Id))
                        continue;

                    AddVirtualEpisode(showInfo, seasonInfo, episodeInfo, season);
                }

                // We add the extras to the season if we're using Shoko Groups.
                if (seriesGrouping) {
                    AddExtras(season, seasonInfo);
                }
            }
        }

        private void UpdateEpisode(Episode episode, string episodeId)
        {
            Info.ShowInfo showInfo = null;
            Info.SeasonInfo seasonInfo = ApiManager.GetSeasonInfoForEpisode(episodeId)
                .GetAwaiter()
                .GetResult();
            Info.EpisodeInfo episodeInfo = seasonInfo.EpisodeList.FirstOrDefault(e => e.Id == episodeId);
            if (Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup)
                showInfo = ApiManager.GetShowInfoForSeries(seasonInfo.Id, Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default)
                    .GetAwaiter()
                    .GetResult();

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

        private IEnumerable<(int, Season)> CreateMissingSeasons(Info.SeasonInfo seasonInfo, Series series, Dictionary<int, Season> existingSeasons, List<int> allSeasonNumbers)
        {
            var missingSeasonNumbers = allSeasonNumbers.Except(existingSeasons.Keys).ToList();
            var mergeFriendly = Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.MergeFriendly && seasonInfo.TvDB != null;
            foreach (var seasonNumber in missingSeasonNumbers) {
                var season = seasonNumber == 1 && !mergeFriendly ? AddVirtualSeason(seasonInfo, 0, 1, series) : AddVirtualSeason(seasonNumber, series);
                if (season == null)
                    continue;
                yield return (seasonNumber, season);
            }
        }

        private IEnumerable<(int, Season)> CreateMissingSeasons(Info.ShowInfo showInfo, Series series, Dictionary<int, Season> seasons)
        {
            bool hasSpecials = false;
            foreach (var pair in showInfo.SeasonOrderDictionary) {
                if (seasons.ContainsKey(pair.Key))
                    continue;
                if (pair.Value.SpecialsList.Count > 0)
                    hasSpecials = true;
                var offset = pair.Key - showInfo.SeasonNumberBaseDictionary[pair.Value];
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

        private void AddVirtualEpisode(Info.ShowInfo showInfo, Info.SeasonInfo seasonInfo, Info.EpisodeInfo episodeInfo, MediaBrowser.Controller.Entities.TV.Season season)
        {
            var groupId = showInfo?.Id ?? null;
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
        #region Extras

        private void AddExtras(Folder parent, Info.SeasonInfo seasonInfo)
        {
            if (seasonInfo.ExtrasList.Count == 0)
                return;

            var needsUpdate = false;
            var extraIds = new List<Guid>();
            foreach (var episodeInfo in seasonInfo.ExtrasList) {
                if (!Lookup.TryGetPathForEpisodeId(episodeInfo.Id, out var episodePath))
                    continue;

                switch (episodeInfo.ExtraType) {
                    default:
                        break;
                    case MediaBrowser.Model.Entities.ExtraType.ThemeSong:
                    case MediaBrowser.Model.Entities.ExtraType.ThemeVideo:
                        if (!parent.SupportsThemeMedia)
                            continue;
                        break;
                }

                var item = LibraryManager.FindByPath(episodePath, false);
                if (item != null && item is Video video) {
                    video.ParentId = Guid.Empty;
                    video.OwnerId = parent.Id;
                    video.Name = episodeInfo.Shoko.Name;
                    video.ExtraType = episodeInfo.ExtraType;
                    video.ProviderIds.TryAdd("Shoko Episode", episodeInfo.Id);
                    video.ProviderIds.TryAdd("Shoko Series", seasonInfo.Id);
                    LibraryManager.UpdateItemAsync(video, null, ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
                    if (!parent.ExtraIds.Contains(video.Id)) {
                        needsUpdate = true;
                        extraIds.Add(video.Id);
                    }
                }
                else {
                    Logger.LogInformation("Adding {ExtraType} {VideoName} to parent {ParentName} (Series={SeriesId})", episodeInfo.ExtraType, episodeInfo.Shoko.Name, parent.Name, seasonInfo.Id);
                    video = new Video {
                        Id = LibraryManager.GetNewItemId($"{parent.Id} {episodeInfo.ExtraType} {episodeInfo.Id}", typeof (Video)),
                        Name = episodeInfo.Shoko.Name,
                        Path = episodePath,
                        ExtraType = episodeInfo.ExtraType,
                        ParentId = Guid.Empty,
                        OwnerId = parent.Id,
                        DateCreated = DateTime.UtcNow,
                        DateModified = DateTime.UtcNow,
                    };
                    video.ProviderIds.Add("Shoko Episode", episodeInfo.Id);
                    video.ProviderIds.Add("Shoko Series", seasonInfo.Id);
                    LibraryManager.CreateItem(video, null);
                    needsUpdate = true;
                    extraIds.Add(video.Id);
                }
            }
            if (needsUpdate) {
                parent.ExtraIds = parent.ExtraIds.Concat(extraIds).Distinct().ToArray();
                LibraryManager.UpdateItemAsync(parent, parent.GetParent(), ItemUpdateType.None, CancellationToken.None).ConfigureAwait(false);
            }
        }

        public void RemoveExtras(Folder parent, string seriesId)
        {
            var searchList = LibraryManager.GetItemList(new InternalItemsQuery {
                IsVirtualItem = false,
                IncludeItemTypes = new [] { Jellyfin.Data.Enums.BaseItemKind.Video },
                HasOwnerId = true,
                HasAnyProviderId = new Dictionary<string, string> { ["Shoko Series"] = seriesId},
                DtoOptions = new DtoOptions(true),
            }, true);

            var deleteOptions = new DeleteOptions {
                DeleteFileLocation = false,
            };

            foreach (var video in searchList)
                LibraryManager.DeleteItem(video, deleteOptions);

            if (searchList.Count > 0)
                Logger.LogInformation("Removed {Count:00} extras from parent {ParentName}. (Series={SeriesId})", searchList.Count, parent.Name, seriesId);
        }

        #endregion
    }
}
