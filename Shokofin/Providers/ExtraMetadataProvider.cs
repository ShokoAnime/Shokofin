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

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            LibraryManager.ItemAdded -= OnLibraryManagerItemAdded;
            LibraryManager.ItemUpdated -= OnLibraryManagerItemUpdated;
            LibraryManager.ItemRemoved -= OnLibraryManagerItemRemoved;
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

        private void OnLibraryManagerItemAdded(object sender, ItemChangeEventArgs itemChangeEventArgs)
        {
            // No action needed if either 1) the setting is turned of, 2) the provider is not enabled for the item
            if (!Plugin.Instance.Configuration.AddMissingMetadata || !IsEnabledForItem(itemChangeEventArgs.Item))
                return;

            switch (itemChangeEventArgs.Item) {
                case Series series: {
                    // Abort if we're unable to get the shoko episode id
                    if (!IsEnabledForSeries(series, out var seriesId))
                        return;

                    UpdateSeries(series, seriesId);
                    break;
                }
                case Season season: {
                    // We're not interested in the dummy season.
                    if (!season.IndexNumber.HasValue)
                        return;

                    if (!(itemChangeEventArgs.Parent is Series series))
                        return;

                    // Abort if we're unable to get the shoko series id
                    if (!IsEnabledForSeason(season, out var seriesId))
                        return;

                    UpdateSeason(season, seriesId, series);
                    break;
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

                    if (Plugin.Instance.Configuration.AddMissingMetadata)
                        RemoveDuplicateEpisodes(series, seriesId);

                    UpdateSeries(series, seriesId);
                    return;
                }
                case Season season: {
                    // We're not interested in the dummy season.
                    if (!season.IndexNumber.HasValue)
                        return;

                    // Abort if we're unable to get the shoko series id
                    if (!IsEnabledForSeason(season, out var seriesId))
                        return;

                    if (Plugin.Instance.Configuration.AddMissingMetadata)
                        RemoveDuplicateEpisodes(season, seriesId);

                    if (!(itemChangeEventArgs.Parent is Series series))
                        return;

                    UpdateSeason(season, seriesId, series);
                    return;
                }
                case Episode episode: {
                    if (!Plugin.Instance.Configuration.AddMissingMetadata)
                        return;

                    // Abort if we're unable to get the shoko episode id
                    if (!IsEnabledForEpisode(episode, out var episodeId))
                        return;

                    RemoveDuplicateEpisodes(episode, episodeId);
                    return;
                }
            }
        }

        private void OnLibraryManagerItemRemoved(object sender, ItemChangeEventArgs itemChangeEventArgs)
        {
            if (itemChangeEventArgs.Item.IsVirtualItem)
                return;

            switch (itemChangeEventArgs.Item) {
                // Clean up after removing a series.
                case Series series: {
                    if (!IsEnabledForSeries(series, out var seriesId))
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
                    if (!(IsEnabledForSeason(season, out var seriesId) && (itemChangeEventArgs.Parent is Series series)))
                        return;

                    if (itemChangeEventArgs.UpdateReason == ItemUpdateType.None)
                        RemoveExtras(season, seriesId);
                    else
                        UpdateSeason(season, seriesId, series, true);
                    break;
                }
                // Similarly, create a new virtual episode if the real one was deleted.
                case Episode episode: {
                    if (!IsEnabledForEpisode(episode, out var episodeId))
                        return;

                    RemoveDuplicateEpisodes(episode, episodeId);

                    UpdateEpisode(episode, episodeId);
                    break;
                }
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

        private void UpdateSeries(Series series, string seriesId)
        {
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
                        foreach (var pair in groupInfo.SeasonOrderDictionary) {
                            var seasonNumber= pair.Key;
                            if (!seasons.TryGetValue(seasonNumber, out var season) || season == null)
                                continue;

                            var seriesInfo = pair.Value;
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
                        AddExtras(series, groupInfo.DefaultSeries);

                        foreach (var pair in groupInfo.SeasonOrderDictionary) {
                            if (!seasons.TryGetValue(pair.Key, out var season) || season == null)
                                continue;

                            AddExtras(season, pair.Value);
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
                        foreach (var (seasonNumber, season) in CreateMissingSeasons(seriesInfo, series, seasons, allKnownSeasonNumbers))
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

        private void UpdateSeason(Season season, string seriesId, Series series, bool deleted = false)
        {
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

                    if (addMissing && deleted) {
                        var alternateEpisodes = seasonNumber != groupInfo.SeasonNumberBaseDictionary[seriesInfo];
                        season = seasonNumber == 0 ? AddVirtualSeason(0, series) : AddVirtualSeason(seriesInfo, alternateEpisodes, seasonNumber, series);
                    }
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

        private void UpdateEpisode(Episode episode, string episodeId)
        {
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

        private IEnumerable<(int, Season)> CreateMissingSeasons(Info.SeriesInfo seriesInfo, Series series, Dictionary<int, Season> existingSeasons, List<int> allSeasonNumbers)
        {
            var missingSeasonNumbers = allSeasonNumbers.Except(existingSeasons.Keys).ToList();
            var mergeFriendly = Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.MergeFriendly && seriesInfo.TvDB != null;
            foreach (var seasonNumber in missingSeasonNumbers) {
                var season = seasonNumber == 1 && !mergeFriendly ? AddVirtualSeason(seriesInfo, false, 1, series) : AddVirtualSeason(seasonNumber, series);
                if (season == null)
                    continue;
                yield return (seasonNumber, season);
            }
        }

        private IEnumerable<(int, Season)> CreateMissingSeasons(Info.GroupInfo groupInfo, Series series, Dictionary<int, Season> seasons)
        {
            bool hasSpecials = false;
            foreach (var pair in groupInfo.SeasonOrderDictionary) {
                if (seasons.ContainsKey(pair.Key))
                    continue;
                if (pair.Value.SpecialsList.Count > 0)
                    hasSpecials = true;
                var alternateEpisodes = pair.Key != groupInfo.SeasonNumberBaseDictionary[pair.Value];
                var season = AddVirtualSeason(pair.Value, alternateEpisodes, pair.Key, series);
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
                IncludeItemTypes = new [] { nameof (Season) },
                IndexNumber = seasonNumber,
                SeriesPresentationUniqueKey = seriesPresentationUniqueKey,
                DtoOptions = new DtoOptions(true),
            }, true);

            if (searchList.Count > 0) {
                Logger.LogDebug("Season {SeasonName} for Series {SeriesName} was created in another concurrent thread, skipping.", searchList[0].Name, seriesName);
                return true;
            }

            return false;
        }

        private Season AddVirtualSeason(int seasonNumber, Series series)
        {
            var seriesPresentationUniqueKey = series.GetPresentationUniqueKey();
            if (SeasonExists(seriesPresentationUniqueKey, series.Name, seasonNumber))
                return null;

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
                    series.Id + "Season " + seasonNumber.ToString(CultureInfo.InvariantCulture),
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

        private Season AddVirtualSeason(Info.SeriesInfo seriesInfo, bool alternateEpisodes, int seasonNumber, Series series)
        {
            var seriesPresentationUniqueKey = series.GetPresentationUniqueKey();
            if (SeasonExists(seriesPresentationUniqueKey, series.Name, seasonNumber))
                return null;

            var ( displayTitle, alternateTitle ) = Text.GetSeriesTitles(seriesInfo.AniDB.Titles, seriesInfo.Shoko.Name, series.GetPreferredMetadataLanguage());
            var sortTitle = $"S{seasonNumber} - {seriesInfo.Shoko.Name}";

            if (alternateEpisodes) {
                displayTitle += " (Other Episodes)";
                alternateTitle += " (Other Episodes)";
            }

            Logger.LogInformation("Adding virtual season {SeasonName} entry for {SeriesName}", displayTitle, series.Name);
            var season = new Season {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                IndexNumber = seasonNumber,
                SortName = sortTitle,
                ForcedSortName = sortTitle,
                Id = LibraryManager.GetNewItemId(
                    series.Id + "Season " + seasonNumber.ToString(CultureInfo.InvariantCulture),
                    typeof(Season)),
                IsVirtualItem = true,
                Overview = Text.GetDescription(seriesInfo),
                PremiereDate = seriesInfo.AniDB.AirDate,
                EndDate = seriesInfo.AniDB.EndDate,
                ProductionYear = seriesInfo.AniDB.AirDate?.Year,
                Tags = seriesInfo.Tags.ToArray(),
                Genres = seriesInfo.Genres.ToArray(),
                Studios = seriesInfo.Studios.ToArray(),
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

        private void AddExtras(Folder parent, Info.SeriesInfo seriesInfo)
        {
            if (seriesInfo.ExtrasList.Count == 0)
                return;

            var needsUpdate = false;
            var extraIds = new List<Guid>();
            foreach (var episodeInfo in seriesInfo.ExtrasList) {
                if (!ApiManager.TryGetEpisodePathForId(episodeInfo.Id, out var episodePath))
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
                    video.ProviderIds.TryAdd("Shoko Series", seriesInfo.Id);
                    LibraryManager.UpdateItemAsync(video, null, ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
                    if (!parent.ExtraIds.Contains(video.Id)) {
                        needsUpdate = true;
                        extraIds.Add(video.Id);
                    }
                }
                else {
                    Logger.LogInformation("Addding {ExtraType} {VideoName} to parent {ParentName} (Series={SeriesId})", episodeInfo.ExtraType, parent.Name, seriesInfo.Id);
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
                    video.ProviderIds.Add("Shoko Series", seriesInfo.Id);
                    LibraryManager.CreateItem(video, null);
                    needsUpdate = true;
                    extraIds.Add(video.Id);
                }
            }
            if (needsUpdate) {
                parent.ExtraIds = parent.ExtraIds.Concat(extraIds).Distinct().ToArray();
                LibraryManager.UpdateItemAsync(parent, parent.Parent, ItemUpdateType.None, CancellationToken.None).ConfigureAwait(false);
            }
        }

        public void RemoveExtras(Folder parent, string seriesId)
        {
            var searchList = LibraryManager.GetItemList(new InternalItemsQuery {
                IsVirtualItem = false,
                IncludeItemTypes = new [] { nameof (Video) },
                HasOwnerId = true,
                HasAnyProviderId = { ["Shoko Series"] = seriesId},
                DtoOptions = new DtoOptions(true),
            }, true);

            var deleteOptions = new DeleteOptions {
                DeleteFileLocation = false,
            };

            foreach (var video in searchList)
                LibraryManager.DeleteItem(video, deleteOptions);

            if (searchList.Count > 0)
                Logger.LogInformation("Removed {Count} extras from parent {ParentName}. (Series={SeriesId})", searchList.Count, parent.Name, seriesId);
        }

        private void RemoveDuplicateEpisodes(Episode episode, string episodeId)
        {
            var query = new InternalItemsQuery {
                        IsVirtualItem = true,
                ExcludeItemIds = new [] { episode.Id },
                HasAnyProviderId = { ["Shoko Episode"] = episodeId },
                IncludeItemTypes = new [] { nameof (Episode) },
                GroupByPresentationUniqueKey = false,
                DtoOptions = new DtoOptions(true),
            };

            var existingVirtualItems = LibraryManager.GetItemList(query);

            var deleteOptions = new DeleteOptions {
                DeleteFileLocation = true,
            };

            // Remove the virtual season/episode that matches the newly updated item
            foreach (var item in existingVirtualItems)
                LibraryManager.DeleteItem(item, deleteOptions);

            if (existingVirtualItems.Count > 0)
                Logger.LogInformation("Removed {Count} duplicate episodes for episode {EpisodeName}. (Episode={EpisodeId})", existingVirtualItems.Count, episode.Name, episodeId);
        }

        private void RemoveDuplicateEpisodes(Season season, string seriesId)
        {
            var seasonId = $"{seriesId}:{season.IndexNumber.Value}";
            if (!ApiManager.TryLockActionForIdOFType("season", seasonId, "remove"))
                return;

            try {
                foreach (var episode in season.GetEpisodes(null, new DtoOptions(true)).OfType<Episode>()) {
                    // We're only interested in physical episodes.
                    if (episode.IsVirtualItem)
                        continue;

                    // Abort if we're unable to get the shoko episode id
                    if (!IsEnabledForEpisode(episode, out var episodeId))
                        continue;

                    RemoveDuplicateEpisodes(episode, episodeId);
                }
            }
            finally {
                ApiManager.TryUnlockActionForIdOFType("season", seasonId, "remove");
            }
        }

        private void RemoveDuplicateEpisodes(Series series, string seriesId)
        {
            if (!ApiManager.TryLockActionForIdOFType("series", seriesId, "remove"))
                return;

            try {
                foreach (var season in series.GetSeasons(null, new DtoOptions(true)).OfType<Season>()) {
                    // We're not interested in any dummy seasons
                    if (!season.IndexNumber.HasValue)
                        continue;

                    RemoveDuplicateEpisodes(season, seriesId);
                }
            }
            finally {
                ApiManager.TryUnlockActionForIdOFType("series", seriesId, "remove");
            }
        }
    }
}