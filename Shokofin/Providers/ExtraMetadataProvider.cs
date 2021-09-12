using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Events;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
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

        private readonly ILibraryManager LibraryManager;

        private readonly IProviderManager ProviderManager;

        private readonly ILocalizationManager LocalizationManager;

        private readonly ILogger<ExtraMetadataProvider> Logger;

        public ExtraMetadataProvider(ShokoAPIManager apiManager, ILibraryManager libraryManager, IProviderManager providerManager, ILocalizationManager localizationManager, ILogger<ExtraMetadataProvider> logger)
        {
            ApiManager = apiManager;
            LibraryManager = libraryManager;
            ProviderManager = providerManager;
            LocalizationManager = localizationManager;
            Logger = logger;
        }

        public Task RunAsync()
        {
            LibraryManager.ItemAdded += OnLibraryManagerItemAdded;
            LibraryManager.ItemUpdated += OnLibraryManagerItemUpdated;
            LibraryManager.ItemRemoved += OnLibraryManagerItemRemoved;
            ProviderManager.RefreshCompleted += OnProviderManagerRefreshComplete;

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            LibraryManager.ItemAdded -= OnLibraryManagerItemAdded;
            LibraryManager.ItemUpdated -= OnLibraryManagerItemUpdated;
            LibraryManager.ItemRemoved -= OnLibraryManagerItemRemoved;
            ProviderManager.RefreshCompleted -= OnProviderManagerRefreshComplete;
        }

        public bool IsEnabledForItem(BaseItem item)
        {
            if (item == null)
                return false;

            BaseItem seriesOrItem = item switch
            {
                Episode e => e.Series,
                Series s => s,
                Season s => s.Series,
                _ => item,
            };

            if (seriesOrItem == null)
                return false;

            var libraryOptions = LibraryManager.GetLibraryOptions(seriesOrItem);
            return libraryOptions != null && libraryOptions.TypeOptions.Any(o => o.Type == nameof (Series) && o.MetadataFetchers.Contains(Plugin.MetadataProviderName));
        }

        private void OnProviderManagerRefreshComplete(object sender, GenericEventArgs<BaseItem> genericEventArgs)
        {
            // No action needed if either 1) the setting is turned of, 2) the provider is not enabled for the item
            if (!Plugin.Instance.Configuration.AddMissingMetadata || !IsEnabledForItem(genericEventArgs.Argument))
                return;

            switch (genericEventArgs.Argument) {
                case Series series:
                    HandleSeries(series);
                    break;
                case Season season:
                    HandleSeason(season, season.Series);
                    break;
            }
        }

        private void OnLibraryManagerItemAdded(object sender, ItemChangeEventArgs itemChangeEventArgs)
        {
            if (!Plugin.Instance.Configuration.AddMissingMetadata || !IsEnabledForItem(itemChangeEventArgs.Item))
                return;

            switch (itemChangeEventArgs.Item) {
                case Episode episode: {
                    // Abort if we're unable to get the shoko episode id
                    if (!IsEnabledForEpisode(episode, out var episodeId))
                        return;

                    var query = new InternalItemsQuery {
                        IsVirtualItem = true,
                        HasAnyProviderId = { ["Shoko Episode"] = episodeId },
                        IncludeItemTypes = new [] { nameof (Episode) },
                        GroupByPresentationUniqueKey = false,
                        DtoOptions = new DtoOptions(true),
                    };

                    var existingVirtualItems = LibraryManager.GetItemList(query);
                    var deleteOptions = new DeleteOptions {
                        DeleteFileLocation = true,
                    };

                    // Remove the old virtual episode that matches the newly created item
                    foreach (var item in existingVirtualItems) {
                        if (episode.IsVirtualItem && System.Guid.Equals(item.Id, episode.Id))
                            continue;

                        LibraryManager.DeleteItem(item, deleteOptions);
                    }
                    break;
                }
            }
        }

        private void OnLibraryManagerItemUpdated(object sender, ItemChangeEventArgs itemChangeEventArgs)
        {
            if (!Plugin.Instance.Configuration.AddMissingMetadata || !IsEnabledForItem(itemChangeEventArgs.Item))
                return;

            switch (itemChangeEventArgs.Item) {
                case Series series: {
                    // Abort if we're unable to get the shoko episode id
                    if (!IsEnabledForSeries(series, out var seriesId))
                        return;

                    if (!ApiManager.TryLockActionForIdOFType("series", seriesId, "remove"))
                        return;

                    try {
                        foreach (var season in series.GetSeasons(null, new DtoOptions(true))) {
                            OnLibraryManagerItemUpdated(this, new ItemChangeEventArgs { Item = season, Parent = series, UpdateReason = ItemUpdateType.None });
                        }
                    }
                    finally {
                        ApiManager.TryUnlockActionForIdOFType("series", seriesId, "remove");
                    }

                    return;
                }
                case Season season: {
                    // We're not interested in the dummy season.
                    if (!season.IndexNumber.HasValue)
                        return;

                    // Abort if we're unable to get the shoko episode id
                    if (!IsEnabledForSeason(season, out var seriesId))
                        return;

                    var seasonId = $"{seriesId}:{season.IndexNumber.Value}";
                    if (!ApiManager.TryLockActionForIdOFType("season", seasonId, "remove"))
                        return;

                    try {
                        foreach (var episode in season.GetEpisodes(null, new DtoOptions(true)).Where(ep => !ep.IsVirtualItem)) {
                            OnLibraryManagerItemUpdated(this, new ItemChangeEventArgs { Item = episode, Parent = season, UpdateReason = ItemUpdateType.None });
                        }
                    }
                    finally {
                        ApiManager.TryUnlockActionForIdOFType("season", seasonId, "remove");
                    }

                    return;
                }
                case Episode episode: {
                    // Abort if we're unable to get the shoko episode id
                    if (!IsEnabledForEpisode(episode, out var episodeId))
                        return;

                    var query = new InternalItemsQuery {
                        IsVirtualItem = true,
                        HasAnyProviderId = { ["Shoko Episode"] = episodeId },
                        IncludeItemTypes = new [] { nameof (Episode) },
                        GroupByPresentationUniqueKey = false,
                        DtoOptions = new DtoOptions(true),
                    };

                    var existingVirtualItems = LibraryManager.GetItemList(query);

                    var deleteOptions = new DeleteOptions {
                        DeleteFileLocation = true,
                    };

                    var count = existingVirtualItems.Count;
                    // Remove the virtual season/episode that matches the newly updated item
                    foreach (var item in existingVirtualItems) {
                        if (episode.IsVirtualItem && System.Guid.Equals(item.Id, episode.Id)) {
                            count--;
                            continue;
                        }

                        LibraryManager.DeleteItem(item, deleteOptions);
                    }
                    Logger.LogInformation("Removed {Count} duplicate episodes for episode {EpisodeName}. (Episode={EpisodeId})", count, episode.Name, episodeId);

                    return;
                }
            }
        }

        private void OnLibraryManagerItemRemoved(object sender, ItemChangeEventArgs itemChangeEventArgs)
        {
            // No action needed if either 1) the setting is turned of, 2) the item is virtual, 3) the provider is not enabled for the item
            if (!Plugin.Instance.Configuration.AddMissingMetadata || itemChangeEventArgs.Item.IsVirtualItem || !IsEnabledForItem(itemChangeEventArgs.Item))
                return;

            switch (itemChangeEventArgs.Item) {
                // Create a new virtual season if the real one was deleted.
                case Season season:
                    HandleSeason(season, itemChangeEventArgs.Parent as Series, true);
                    break;
                // Similarly, create a new virtual episode if the real one was deleted.
                case Episode episode:
                    HandleEpisode(episode);
                    break;
            }
        }

        private bool IsEnabledForSeries(Series series, out string seriesId)
        {
            if (series.ProviderIds.TryGetValue("Shoko Series", out seriesId) && !string.IsNullOrEmpty(seriesId)) {
                return true;
            }

            if (ApiManager.TryGetSeriesIdForPath(series.Path, out seriesId)) {
                // Set the "Shoko Group" and "Shoko Series" provider ids for the series, since it haven't been set again. It doesn't matter if it's not saved to the database, since we only need it while running the following code.
                if (ApiManager.TryGetGroupIdForSeriesId(seriesId, out var groupId)) {
                    var filterByType = Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default;
                    var groupInfo = ApiManager.GetGroupInfoSync(groupId, filterByType);
                    seriesId = groupInfo.DefaultSeries.Id;

                    SeriesProvider.AddProviderIds(series, seriesId, groupInfo.Id);
                }
                // Same as above, but only set the "Shoko Series" id.
                else {
                    SeriesProvider.AddProviderIds(series, seriesId);
                }
                // Make sure the presentation unique is not cached, so we won't reuse the cache key.
                series.PresentationUniqueKey = null;
                return true;
            }

            return false;
        }

        private void HandleSeries(Series series)
        {
            // Abort if we're unable to get the series id
            if (!IsEnabledForSeries(series, out var seriesId))
                return;

            if (!ApiManager.TryLockActionForIdOFType("series", seriesId, "update"))
                return;

            try {
                // Provide metadata for a series using Shoko's Group feature
                if (Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup) {
                    var groupInfo = ApiManager.GetGroupInfoForSeriesSync(seriesId, Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default);
                    if (groupInfo == null) {
                        Logger.LogWarning("Unable to find group info for series. (Series={SeriesID})", seriesId);
                        return;
                    }
                    // Get the existing seasons and episode ids
                    var (seasons, episodeIds) = GetExistingSeasonsAndEpisodeIds(series);

                    if (Plugin.Instance.Configuration.AddMissingMetadata) {
                        // Add missing seasons
                        foreach (var (seasonNumber, season) in CreateMissingSeasons(groupInfo, series, seasons))
                            seasons.TryAdd(seasonNumber, season);

                        // Handle specials when grouped.
                        if (seasons.TryGetValue(0, out var zeroSeason)) {
                            var seasonId = $"{seriesId}:0";
                            if (ApiManager.TryLockActionForIdOFType("season", seasonId, "update")) {
                                try {
                                    foreach (var seriesInfo in groupInfo.SeriesList) {
                                        foreach (var episodeInfo in seriesInfo.SpecialsList) {
                                            if (episodeIds.Contains(episodeInfo.Id))
                                                continue;

                                            AddVirtualEpisode(groupInfo, seriesInfo, episodeInfo, zeroSeason);
                                        }
                                    }
                                }
                                finally {
                                    ApiManager.TryUnlockActionForIdOFType("season", seasonId, "update");
                                }
                            }
                        }

                        // Add missing episodes
                        foreach (var (seriesInfo, index) in groupInfo.SeriesList.Select((s, i) => (s, i))) {
                            var value = index - groupInfo.DefaultSeriesIndex;
                            var seasonNumber = value < 0 ? value : value + 1;
                            if (!seasons.TryGetValue(seasonNumber, out var season) || season == null)
                                continue;

                            var seasonId = $"{seriesId}:{seasonNumber}";
                            if (ApiManager.TryLockActionForIdOFType("season", seasonId, "update")) {
                                try {
                                    foreach (var episodeInfo in seriesInfo.EpisodeList) {
                                        if (episodeIds.Contains(episodeInfo.Id))
                                            continue;

                                        AddVirtualEpisode(groupInfo, seriesInfo, episodeInfo, season);
                                    }
                                }
                                finally {
                                    ApiManager.TryUnlockActionForIdOFType("season", seasonId, "update");
                                }
                            }
                        }
                    }

                    // We add the extras to the season if we're using Shoko Groups.
                    if (Plugin.Instance.Configuration.AddExtraVideos) {
                        foreach (var (seriesInfo, index) in groupInfo.SeriesList.Select((s, i) => (s, i))) {
                            var value = index - groupInfo.DefaultSeriesIndex;
                            var seasonNumber = value < 0 ? value : value + 1;
                            if (!seasons.TryGetValue(seasonNumber, out var season) || season == null)
                                continue;

                            AddExtras(season, seriesInfo);
                        }
                    }
                }
                // Provide metadata for other series
                else {
                    var seriesInfo = ApiManager.GetSeriesInfoSync(seriesId);
                    if (seriesInfo == null) {
                        Logger.LogWarning("Unable to find series info. (Series={SeriesID})", seriesId);
                        return;
                    }

                    if (Plugin.Instance.Configuration.AddMissingMetadata) {
                        // Get the existing seasons and episode ids
                        var (seasons, episodeIds) = GetExistingSeasonsAndEpisodeIds(series);

                        // Compute the season numbers for each episode in the series in advance, since we need to filter out the missing seasons
                        var episodeInfoToSeasonNumberDirectory = seriesInfo.RawEpisodeList.ToDictionary(e => e, e => Ordering.GetSeasonNumber(null, seriesInfo, e));

                        // Add missing seasons
                        var allKnownSeasonNumbers = episodeInfoToSeasonNumberDirectory.Values.Distinct().ToList();
                        foreach (var (seasonNumber, season) in CreateMissingSeasons(series, seasons, allKnownSeasonNumbers))
                            seasons.Add(seasonNumber, season);

                        // Add missing episodes
                        foreach (var episodeInfo in seriesInfo.RawEpisodeList) {
                            if (episodeInfo.ExtraType != null)
                                continue;

                            if (episodeIds.Contains(episodeInfo.Id))
                                continue;

                            var seasonNumber = episodeInfoToSeasonNumberDirectory[episodeInfo];
                            if (!seasons.TryGetValue(seasonNumber, out var season) || season == null)
                                continue;

                            AddVirtualEpisode(null, seriesInfo, episodeInfo, season);
                        }
                    }

                    // We add the extras to the series if not.
                    if (Plugin.Instance.Configuration.AddExtraVideos) {
                        AddExtras(series, seriesInfo);
                    }
                }
            }
            finally {
                ApiManager.TryUnlockActionForIdOFType("series", seriesId, "update");
            }
        }

        private bool IsEnabledForSeason(Season season, out string seriesId)
        {
            if (!season.IndexNumber.HasValue) {
                seriesId = null;
                return false;
            }
            return IsEnabledForSeries(season.Series, out seriesId);
        }

        private void HandleSeason(Season season, Series series, bool deleted = false)
        {
            if (!IsEnabledForSeason(season, out var seriesId))
                return;

            var seasonId = $"{seriesId}:{season.IndexNumber.Value}";
            try {
                if (!ApiManager.TryLockActionForIdOFType("season", seasonId, "update"))
                    return;

                var seasonNumber = season.IndexNumber!.Value;
                var addMissing = Plugin.Instance.Configuration.AddMissingMetadata;
                var seriesGrouping = Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup;
                Info.GroupInfo groupInfo = null;
                Info.SeriesInfo seriesInfo = null;
                // Provide metadata for a season using Shoko's Group feature
                if (seriesGrouping) {
                    groupInfo = ApiManager.GetGroupInfoForSeriesSync(seriesId, Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default);
                    if (groupInfo == null) {
                        Logger.LogWarning("Unable to find group info for series. (Series={SeriesId})", seriesId);
                        return;
                    }


                    seriesInfo = groupInfo.GetSeriesInfoBySeasonNumber(seasonNumber);
                    if (seriesInfo == null) {
                        Logger.LogWarning("Unable to find series info for {SeasonNumber} in group for series. (Group={GroupId})", seasonNumber, groupInfo.Id);
                        return;
                    }

                    if (addMissing && deleted)
                        season = seasonNumber == 0 ? AddVirtualSeason(0, series) : AddVirtualSeason(seriesInfo, seasonNumber, series);
                }
                // Provide metadata for other seasons
                else {
                    seriesInfo = ApiManager.GetSeriesInfoSync(seriesId);
                    if (seriesInfo == null) {
                        Logger.LogWarning("Unable to find series info. (Series={SeriesId})", seriesId);
                        return;
                    }

                    if (addMissing && deleted)
                        season = AddVirtualSeason(seasonNumber, series);
                }

                if (addMissing) {
                    // Get a hash-set of existing episodes – both physical and virtual – to exclude when adding new virtual episodes.
                    var existingEpisodes = new HashSet<string>();
                    foreach (var episode in season.Children.OfType<Episode>())
                        if (IsEnabledForEpisode(episode, out var episodeId))
                            existingEpisodes.Add(episodeId);

                    // Handle specials when grouped.
                    if (seasonNumber == 0) {
                        if (seriesGrouping) {
                            foreach (var sI in groupInfo.SeriesList) {
                                foreach (var episodeInfo in sI.SpecialsList) {
                                    if (existingEpisodes.Contains(episodeInfo.Id))
                                        continue;

                                    AddVirtualEpisode(groupInfo, seriesInfo, episodeInfo, season);
                                }
                            }
                        }
                        else {
                            foreach (var episodeInfo in seriesInfo.SpecialsList) {
                                if (existingEpisodes.Contains(episodeInfo.Id))
                                    continue;

                                AddVirtualEpisode(groupInfo, seriesInfo, episodeInfo, season);
                            }
                        }

                        return;
                    }

                    foreach (var episodeInfo in seriesInfo.EpisodeList) {
                        var episodeParentIndex = Ordering.GetSeasonNumber(groupInfo, seriesInfo, episodeInfo);
                        if (episodeParentIndex != seasonNumber)
                            continue;

                        if (existingEpisodes.Contains(episodeInfo.Id))
                            continue;

                        AddVirtualEpisode(groupInfo, seriesInfo, episodeInfo, season);
                    }
                }

                // We add the extras to the season if we're using Shoko Groups.
                if (Plugin.Instance.Configuration.AddExtraVideos && seriesGrouping) {
                    AddExtras(season, seriesInfo);
                }
            }
            finally {
                ApiManager.TryUnlockActionForIdOFType("season", seasonId, "update");
            }

        }

        private bool IsEnabledForEpisode(Episode episode, out string episodeId)
        {
            return (
                // This will account for virtual episodes and existing episodes
                episode.ProviderIds.TryGetValue("Shoko Episode", out episodeId) ||
                // This will account for new episodes that haven't received their first metadata update yet
                ApiManager.TryGetEpisodeIdForPath(episode.Path, out episodeId)
            ) && !string.IsNullOrEmpty(episodeId);
        }

        private void HandleEpisode(Episode episode)
        {
            // Abort if we're unable to get the shoko episode id
            if (!IsEnabledForEpisode(episode, out var episodeId))
                return;

            Info.GroupInfo groupInfo = null;
            Info.SeriesInfo seriesInfo = ApiManager.GetSeriesInfoForEpisodeSync(episodeId);
            Info.EpisodeInfo episodeInfo = seriesInfo.EpisodeList.Find(e => e.Id == episodeId);
            if (Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup)
                groupInfo = ApiManager.GetGroupInfoForSeriesSync(seriesInfo.Id, Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default);

            AddVirtualEpisode(groupInfo, seriesInfo, episodeInfo, episode.Season);
        }

        private (Dictionary<int, Season>, HashSet<string>) GetExistingSeasonsAndEpisodeIds(Series series)
        {
            var seasons = new Dictionary<int, Season>();
            var episodes = new HashSet<string>();
            foreach (var item in series.GetRecursiveChildren()) switch (item) {
                case Season season:
                    if (season.IndexNumber.HasValue)
                        seasons.TryAdd(season.IndexNumber.Value, season);
                    break;
                case Episode episode:
                    // Get a hash-set of existing episodes – both physical and virtual – to exclude when adding new virtual episodes.
                    if (IsEnabledForEpisode(episode, out var episodeId))
                        episodes.Add(episodeId);
                    break;
            }
            return (seasons, episodes);
        }

        private IEnumerable<(int, Season)> CreateMissingSeasons(Series series, Dictionary<int, Season> existingSeasons, List<int> allSeasonNumbers)
        {
            var missingSeasonNumbers = allSeasonNumbers.Except(existingSeasons.Keys).ToList();
            foreach (var seasonNumber in missingSeasonNumbers) {
                var season = AddVirtualSeason(seasonNumber, series);
                if (season == null)
                    continue;
                yield return (seasonNumber, season);
            }
        }

        private IEnumerable<(int, Season)> CreateMissingSeasons(Info.GroupInfo groupInfo, Series series, Dictionary<int, Season> seasons)
        {
            bool hasSpecials = false;
            foreach (var (s, index) in groupInfo.SeriesList.Select((a, b) => (a, b))) {
                var value = index - groupInfo.DefaultSeriesIndex;
                var seasonNumber = value < 0 ? value : value + 1;
                if (seasons.ContainsKey(seasonNumber))
                    continue;
                if (s.SpecialsList.Count > 0)
                    hasSpecials = true;
                var season = AddVirtualSeason(s, seasonNumber, series);
                if (season == null)
                    continue;
                yield return (seasonNumber, season);
            }
            if (hasSpecials && !seasons.ContainsKey(0))
                yield return (0, AddVirtualSeason(0, series));
        }

        private Season AddVirtualSeason(int seasonNumber, Series series)
        {
            var seriesPresentationUniqueKey = series.GetPresentationUniqueKey();
            var searchList = LibraryManager.GetItemList(new InternalItemsQuery {
                IncludeItemTypes = new [] { nameof (Season) },
                IndexNumber = seasonNumber,
                SeriesPresentationUniqueKey = seriesPresentationUniqueKey,
                DtoOptions = new DtoOptions(true),
            }, true);

            if (searchList.Count > 0) {
                Logger.LogDebug("Season {SeasonName} for Series {SeriesName} was created in another concurrent thread, skipping.", searchList[0].Name, series.Name);
                return null;
            }

            string seasonName;
            if (seasonNumber == 0)
                seasonName = LibraryManager.GetLibraryOptions(series).SeasonZeroDisplayName;
            else
                seasonName = string.Format(
                    LocalizationManager.GetLocalizedString("NameSeasonNumber"),
                    seasonNumber.ToString(CultureInfo.InvariantCulture));

            Logger.LogInformation("Creating virtual season {SeasonName} entry for {SeriesName}", seasonName, series.Name);

            var season = new Season {
                Name = seasonName,
                IndexNumber = seasonNumber,
                SortName = seasonName,
                ForcedSortName = seasonName,
                Id = LibraryManager.GetNewItemId(
                    series.Id + seasonNumber.ToString(CultureInfo.InvariantCulture) + seasonName,
                    typeof(Season)),
                IsVirtualItem = true,
                SeriesId = series.Id,
                SeriesName = series.Name,
                SeriesPresentationUniqueKey = series.GetPresentationUniqueKey(),
                DateModified = DateTime.UtcNow,
                DateLastSaved = DateTime.UtcNow,
            };

            series.AddChild(season, CancellationToken.None);

            return season;
        }

        private Season AddVirtualSeason(Info.SeriesInfo seriesInfo, int seasonNumber, Series series)
        {
            var seriesPresentationUniqueKey = series.GetPresentationUniqueKey();
            var searchList = LibraryManager.GetItemList(new InternalItemsQuery {
                IncludeItemTypes = new [] { nameof (Season) },
                HasAnyProviderId = { ["Shoko Series"] = seriesInfo.Id },
                SeriesPresentationUniqueKey = seriesPresentationUniqueKey,
                DtoOptions = new DtoOptions(true),
            }, true);

            if (searchList.Count > 0) {
                Logger.LogDebug("Season {SeasonName} for Series {SeriesName} was created in another concurrent thread, skipping.", searchList[0].Name, series.Name);
                return null;
            }

            var tags = ApiManager.GetTags(seriesInfo.Id).GetAwaiter().GetResult();
            var ( displayTitle, alternateTitle ) = Text.GetSeriesTitles(seriesInfo.AniDB.Titles, seriesInfo.Shoko.Name, series.GetPreferredMetadataLanguage());
            var sortTitle = $"S{seasonNumber} - {seriesInfo.Shoko.Name}";

            Logger.LogInformation("Adding virtual season {SeasonName} entry for {SeriesName}", displayTitle, series.Name);
            var season = new Season {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                IndexNumber = seasonNumber,
                SortName = sortTitle,
                ForcedSortName = sortTitle,
                Id = LibraryManager.GetNewItemId(
                    series.Id + "Season " + seriesInfo.Id.ToString(CultureInfo.InvariantCulture),
                    typeof(Season)),
                IsVirtualItem = true,
                Overview = Text.GetDescription(seriesInfo),
                PremiereDate = seriesInfo.AniDB.AirDate,
                EndDate = seriesInfo.AniDB.EndDate,
                ProductionYear = seriesInfo.AniDB.AirDate?.Year,
                Tags = tags,
                CommunityRating = seriesInfo.AniDB.Rating?.ToFloat(10),
                SeriesId = series.Id,
                SeriesName = series.Name,
                SeriesPresentationUniqueKey = seriesPresentationUniqueKey,
                DateModified = DateTime.UtcNow,
                DateLastSaved = DateTime.UtcNow,
            };
            season.ProviderIds.Add("Shoko Series", seriesInfo.Id);
            if (Plugin.Instance.Configuration.AddAniDBId)
                season.ProviderIds.Add("AniDB", seriesInfo.AniDB.ID.ToString());

            series.AddChild(season, CancellationToken.None);

            return season;
        }

        private void AddVirtualEpisode(Info.GroupInfo groupInfo, Info.SeriesInfo seriesInfo, Info.EpisodeInfo episodeInfo, MediaBrowser.Controller.Entities.TV.Season season)
        {
            var groupId = groupInfo?.Id ?? null;
            var searchList = LibraryManager.GetItemList(new InternalItemsQuery {
                IncludeItemTypes = new [] { nameof (Episode) },
                HasAnyProviderId = { ["Shoko Episode"] = episodeInfo.Id },
                DtoOptions = new DtoOptions(true)
            }, true);

            if (searchList.Count > 0) {
                Logger.LogDebug("A virtual or physical episode entry already exists. Ignoreing. (Episode={EpisodeId},Series={SeriesId},Group={GroupId})", episodeInfo.Id, seriesInfo.Id, groupId);
                return;
            }

            var episodeId = LibraryManager.GetNewItemId(season.Series.Id + "Season " + seriesInfo.Id + " Episode " + episodeInfo.Id, typeof(Episode));
            var result = EpisodeProvider.CreateMetadata(groupInfo, seriesInfo, episodeInfo, season, episodeId);

            Logger.LogInformation("Creating virtual episode for {SeriesName} S{SeasonNumber}:E{EpisodeNumber} (Episode={EpisodeId},Series={SeriesId},Group={GroupId}),", groupInfo?.Shoko.Name ?? seriesInfo.Shoko.Name, season.IndexNumber, result.IndexNumber, episodeInfo.Id, seriesInfo.Id, groupId);

            season.AddChild(result, CancellationToken.None);
        }

        private void AddExtras(BaseItem item, Info.SeriesInfo seriesInfo)
        {
            foreach (var episodeInfo in seriesInfo.ExtrasList) {
                if (!ApiManager.TryGetEpisodePathForId(episodeInfo.Id, out var episodePath))
                    continue;
                
                Logger.LogInformation("TODO: Add {ExtraType} to {ItemName}", episodeInfo.ExtraType, item.Name);
                // The extra video is available locally.
            }
        }
    }
}