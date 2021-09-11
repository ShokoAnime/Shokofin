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
    public class MissingMetadataProvider : IServerEntryPoint
    {
        private readonly ShokoAPIManager ApiManager;

        private readonly ILibraryManager LibraryManager;

        private readonly IProviderManager ProviderManager;

        private readonly ILocalizationManager LocalizationManager;

        private readonly ILogger<MissingMetadataProvider> Logger;

        public MissingMetadataProvider(ShokoAPIManager apiManager, ILibraryManager libraryManager, IProviderManager providerManager, ILocalizationManager localizationManager, ILogger<MissingMetadataProvider> logger)
        {
            ApiManager = apiManager;
            LibraryManager = libraryManager;
            ProviderManager = providerManager;
            LocalizationManager = localizationManager;
            Logger = logger;
        }

        public Task RunAsync()
        {
            LibraryManager.ItemUpdated += OnLibraryManagerItemUpdated;
            LibraryManager.ItemRemoved += OnLibraryManagerItemRemoved;
            ProviderManager.RefreshCompleted += OnProviderManagerRefreshComplete;

            return Task.CompletedTask;
        }

        public void Dispose()
        {
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

        // NOTE: Always delete stall metadata, even if we disabled the feature in the settings page.
        private void OnLibraryManagerItemUpdated(object sender, ItemChangeEventArgs itemChangeEventArgs)
        {

            if (!IsEnabledForItem(itemChangeEventArgs.Item))
                return;


            // If the item is an Episode, filter on ParentIndexNumber as well (season number)
            switch (itemChangeEventArgs.Item) {
                // case Season season: {
                //     // Abort if we're unable to get the shoko episode id
                //     if (!IsEnabledForSeason(season, out var seriesId))
                //         return;
                //     // Only interested in non-virtual Seasons and Episodes
                //     if (!season.IndexNumber.HasValue)
                //         return;
                //
                //
                //     break;
                // }
                case Episode episode: {
                    // Abort if we're unable to get the shoko episode id
                    if (!IsEnabledForEpisode(episode, out var episodeId))
                        return;

                    // Only interested in non-virtual Seasons and Episodes
                    if (episode.IsVirtualItem)
                        return;

                    var query = new InternalItemsQuery {
                        IsVirtualItem = true,
                        IndexNumber = episode.IndexNumber,
                        ParentIndexNumber = episode.ParentIndexNumber,
                        IncludeItemTypes = new [] { episode.GetType().Name },
                        Parent = itemChangeEventArgs.Parent,
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
                    break;
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
            return (series.ProviderIds.TryGetValue("Shoko Series", out seriesId) || ApiManager.TryGetSeriesIdForPath(series.Path, out seriesId)) && !string.IsNullOrEmpty(seriesId);
        }

        private void HandleSeries(Series series)
        {
            // Abort if we're unable to get the series id
            if (!IsEnabledForSeries(series, out var seriesId))
                return;

            // Provide metadata for a series using Shoko's Group feature
            if (Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup) {
                var groupInfo = ApiManager.GetGroupInfoForSeriesSync(seriesId, Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default);
                if (groupInfo == null) {
                    Logger.LogWarning("Unable to find group info for series. (Series={SeriesID})", seriesId);
                    return;
                }

                // If the series id did not match, then it was too early to try matching it.
                if (groupInfo.DefaultSeries.Id != seriesId) {
                    Logger.LogInformation("Selected series is not the same as the of the default series in the group. Ignoring series. (Series={SeriesId},Group={GroupId})", seriesId, groupInfo.Id);
                    return;
                }

                // Get the existing seasons and episode ids
                var (seasons, episodeIds) = GetExistingSeasonsAndEpisodeIds(series);

                // Add missing seasons
                foreach (var (seasonNumber, season) in CreateMissingSeasons(groupInfo, series, seasons)) {
                    seasons.TryAdd(seasonNumber, season);
                }

                // Handle specials when grouped.
                if (seasons.TryGetValue(0, out var zeroSeason)) {
                    foreach (var seriesInfo in groupInfo.SeriesList) {
                        foreach (var episodeInfo in seriesInfo.SpecialsList) {
                            if (episodeIds.Contains(episodeInfo.Id))
                                continue;

                            AddVirtualEpisode(groupInfo, seriesInfo, episodeInfo, zeroSeason);
                        }
                    }
                }

                // Add missing episodes
                foreach (var (seriesInfo, index) in groupInfo.SeriesList.Select((s, i) => (s, i))) {
                    var value = index - groupInfo.DefaultSeriesIndex;
                    var seasonNumber = value < 0 ? value : value + 1;
                    if (!seasons.TryGetValue(seasonNumber, out var season) || season == null)
                        continue;

                    foreach (var episodeInfo in seriesInfo.EpisodeList) {
                        if (episodeIds.Contains(episodeInfo.Id))
                            continue;

                        AddVirtualEpisode(groupInfo, seriesInfo, episodeInfo, season);
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

                // Get the existing seasons and episode ids
                var (seasons, episodeIds) = GetExistingSeasonsAndEpisodeIds(series);

                // Compute the season numbers for each episode in the series in advance, since we need to filter out the missing seasons
                var episodeInfoToSeasonNumberDirectory = seriesInfo.EpisodeList.ToDictionary(e => e, e => Ordering.GetSeasonNumber(null, seriesInfo, e));

                // Add missing seasons
                var allKnownSeasonNumbers = episodeInfoToSeasonNumberDirectory.Values.Distinct().ToList();
                foreach (var (seasonNumber, season) in CreateMissingSeasons(series, seasons, allKnownSeasonNumbers)) {
                    seasons.Add(seasonNumber, season);
                }

                // Add missing episodes
                foreach (var episodeInfo in seriesInfo.EpisodeList) {
                    if (episodeIds.Contains(episodeInfo.Id))
                        continue;

                    var seasonNumber = episodeInfoToSeasonNumberDirectory[episodeInfo];
                    if (!seasons.TryGetValue(seasonNumber, out var season) || season == null)
                        continue;

                    AddVirtualEpisode(null, seriesInfo, episodeInfo, season);
                }
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

            var seasonNumber = season.IndexNumber!.Value;
            var existingEpisodes = new HashSet<string>();
            // Get a hash-set of existing episodes – both physical and virtual – to exclude when adding new virtual episodes.
            foreach (var episode in season.Children.OfType<Episode>())
                if (IsEnabledForEpisode(episode, out var episodeId))
                    existingEpisodes.Add(episodeId);

            Info.GroupInfo groupInfo = null;
            Info.SeriesInfo seriesInfo = null;
            // Provide metadata for a season using Shoko's Group feature
            if (Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup) {
                groupInfo = ApiManager.GetGroupInfoForSeriesSync(seriesId, Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default);
                if (groupInfo == null) {
                    Logger.LogWarning("Unable to find group info for series. (Series={SeriesId})", seriesId);
                    return;
                }

                // Handle specials when grouped.
                if (seasonNumber == 0) {
                    if (deleted)
                        season = AddVirtualSeason(0, series);

                    foreach (var sI in groupInfo.SeriesList) {
                        foreach (var episodeInfo in sI.SpecialsList) {
                            if (existingEpisodes.Contains(episodeInfo.Id))
                                continue;

                            AddVirtualEpisode(groupInfo, seriesInfo, episodeInfo, season);
                        }
                    }

                    return;
                }

                seriesInfo = groupInfo.GetSeriesInfoBySeasonNumber(seasonNumber);
                if (seriesInfo == null) {
                    Logger.LogWarning("Unable to find series info for {SeasonNumber} in group for series. (Group={GroupId})", seasonNumber, groupInfo.Id);
                    return;
                }

                if (deleted)
                    season = AddVirtualSeason(seriesInfo, seasonNumber, series);
            }
            // Provide metadata for other seasons
            else {
                seriesInfo = ApiManager.GetSeriesInfoSync(seriesId);
                if (seriesInfo == null) {
                    Logger.LogWarning("Unable to find series info. (Series={SeriesId})", seriesId);
                    return;
                }

                if (deleted)
                    season = AddVirtualSeason(seasonNumber, series);
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
                yield return (seasonNumber, AddVirtualSeason(seasonNumber, series));
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
                yield return (seasonNumber, season);
            }
            if (hasSpecials && !seasons.ContainsKey(0))
                yield return (0, AddVirtualSeason(0, series));
        }

        private Season AddVirtualSeason(int seasonNumber, Series series)
        {
            string seasonName;
            if (seasonNumber == 0)
                seasonName = LibraryManager.GetLibraryOptions(series).SeasonZeroDisplayName;
            else
                seasonName = string.Format(
                    LocalizationManager.GetLocalizedString("NameSeasonNumber"),
                    seasonNumber.ToString(CultureInfo.InvariantCulture));

            Logger.LogInformation("Creating virtual season {SeasonName} entry for {SeriesName}", seasonName, series.Name);

            var result = new Season {
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

            series.AddChild(result, CancellationToken.None);

            return result;
        }

        private Season AddVirtualSeason(Info.SeriesInfo seriesInfo, int seasonNumber, Series series)
        {
            var tags = ApiManager.GetTags(seriesInfo.Id).GetAwaiter().GetResult();
            var ( displayTitle, alternateTitle ) = Text.GetSeriesTitles(seriesInfo.AniDB.Titles, seriesInfo.Shoko.Name, series.GetPreferredMetadataLanguage());
            var sortTitle = $"S{seasonNumber} - {seriesInfo.Shoko.Name}";

            Logger.LogInformation("Adding virtual season {SeasonName} entry for {SeriesName}", displayTitle, series.Name);
            var result = new Season {
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
                SeriesPresentationUniqueKey = series.GetPresentationUniqueKey(),
                DateModified = DateTime.UtcNow,
                DateLastSaved = DateTime.UtcNow,
            };
            result.ProviderIds.Add("Shoko Series", seriesInfo.Id);
            if (Plugin.Instance.Configuration.AddAniDBId)
                result.ProviderIds.Add("AniDB", seriesInfo.AniDB.ID.ToString());

            series.AddChild(result, CancellationToken.None);

            return result;
        }

        private void AddVirtualEpisode(Info.GroupInfo groupInfo, Info.SeriesInfo seriesInfo, Info.EpisodeInfo episodeInfo, MediaBrowser.Controller.Entities.TV.Season season)
        {
            var groupId = groupInfo?.Id ?? null;
            var results = LibraryManager.GetItemList(new InternalItemsQuery { IncludeItemTypes = new [] { nameof (Episode) }, HasAnyProviderId = { ["Shoko Episode"] = episodeInfo.Id }, DtoOptions = new DtoOptions(true) }, true);

            if (results.Count > 0) {
                Logger.LogWarning("A virtual or physical episode entry already exists. Ignoreing. (Episode={EpisodeId},Series={SeriesId},Group={GroupId})", episodeInfo.Id, seriesInfo.Id, groupId);
                return;
            }

            var episodeId = LibraryManager.GetNewItemId(season.Series.Id + "Season " + seriesInfo.Id + " Episode " + episodeInfo.Id, typeof(Episode));
            var result = EpisodeProvider.CreateMetadata(groupInfo, seriesInfo, episodeInfo, season, episodeId);

            Logger.LogInformation("Creating virtual episode for {SeriesName} S{SeasonNumber}:E{EpisodeNumber} (Episode={EpisodeId},Series={SeriesId},Group={GroupId}),", groupInfo?.Shoko.Name ?? seriesInfo.Shoko.Name, season.IndexNumber, result.IndexNumber, episodeInfo.Id, seriesInfo.Id, groupId);

            season.AddChild(result, CancellationToken.None);
        }
    }
}