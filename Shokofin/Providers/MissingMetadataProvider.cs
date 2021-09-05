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
                    HandleSeason(season);
                    break;
            }
        }

        // NOTE: Always delete stall metadata, even if we disabled the feature in the settings page.
        private void OnLibraryManagerItemUpdated(object sender, ItemChangeEventArgs itemChangeEventArgs)
        {
            // Only interested in non-virtual Seasons and Episodes
            if (itemChangeEventArgs.Item.IsVirtualItem
                || !(itemChangeEventArgs.Item is Season || itemChangeEventArgs.Item is Episode))
                return;

            if (!IsEnabledForItem(itemChangeEventArgs.Item))
                return;

            // Abort if we're unable to get the shoko episode id
            if (!itemChangeEventArgs.Item.ProviderIds.TryGetValue("Shoko Episode", out var episodeId) || string.IsNullOrEmpty(episodeId))
                return;

            var indexNumber = itemChangeEventArgs.Item.IndexNumber;

            // If the item is an Episode, filter on ParentIndexNumber as well (season number)
            int? parentIndexNumber = null;
            if (itemChangeEventArgs.Item is Episode)
                parentIndexNumber = itemChangeEventArgs.Item.ParentIndexNumber;

            var query = new InternalItemsQuery {
                IsVirtualItem = true,
                IndexNumber = indexNumber,
                ParentIndexNumber = parentIndexNumber,
                IncludeItemTypes = new [] { itemChangeEventArgs.Item.GetType().Name },
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
        }

        private void OnLibraryManagerItemRemoved(object sender, ItemChangeEventArgs itemChangeEventArgs)
        {
            // No action needed if either 1) the setting is turned of, 2) the item is virtual, 3) the provider is not enabled for the item
            if (!Plugin.Instance.Configuration.AddMissingMetadata || itemChangeEventArgs.Item.IsVirtualItem || !IsEnabledForItem(itemChangeEventArgs.Item))
                return;

            switch (itemChangeEventArgs.Item) {
                // Create a new virtual season if the real one was deleted.
                case Season season:
                    HandleSeason(season, true);
                    break;
                // Similarly, create a new virtual episode if the real one was deleted.
                case Episode episode:
                    HandleEpisode(episode);
                    break;
            }
        }

        private void HandleSeries(Series series)
        {
            // Abort if we're unable to get the series id
            if (!series.ProviderIds.TryGetValue("Shoko Series", out var seriesId) || string.IsNullOrEmpty(seriesId))
                return;

            var seasons = new Dictionary<int, Season>();
            var existingEpisodes = new HashSet<string>();
            foreach (var child in series.GetRecursiveChildren()) switch (child) {
                case Season season:
                    if (season.IndexNumber.HasValue)
                        seasons.TryAdd(season.IndexNumber.Value, season);
                    break;
                case Episode episode:
                    if (!episode.ProviderIds.TryGetValue("Shoko Episode", out var episodeId) || string.IsNullOrEmpty(episodeId))
                        continue;
                    existingEpisodes.Add(episodeId);
                    break;
            }

            // Provider metadata for a series using Shoko's Group feature
            if (Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup) {
                var groupInfo = ApiManager.GetGroupInfoForSeriesSync(seriesId, Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default);

                // Add missing seasons
                foreach (var pair in AddMissingSeasons(groupInfo, series, seasons)) {
                    seasons.Add(pair.Key, pair.Value);
                }

                // Add missing episodes
                foreach (var (seriesInfo, index) in groupInfo.SeriesList.Select((s, i) => (s, i))) {
                    var value = index - groupInfo.DefaultSeriesIndex;
                    var seasonNumber = value < 0 ? value : value + 1;
                    if (!seasons.TryGetValue(seasonNumber, out var season) || season == null)
                        continue;

                    foreach (var episodeInfo in seriesInfo.EpisodeList) {
                        if (existingEpisodes.Contains(episodeInfo.Id))
                            continue;

                        AddVirtualEpisode(groupInfo, seriesInfo, episodeInfo, season);
                    }
                }
            }
            // Provide metadata for other series
            else {
                var seriesInfo = ApiManager.GetSeriesInfoSync(seriesId);

                // Compute the season numbers for each episode in the series in advance, since we need to filter out the missing seasons
                var episodeInfoToSeasonNumberDirectory = seriesInfo.EpisodeList.ToDictionary(e => e, e => Ordering.GetSeasonNumber(null, seriesInfo, e));

                // Add missing seasons
                var allKnownSeasonNumbers = episodeInfoToSeasonNumberDirectory.Values.Distinct().ToList();
                foreach (var pair in AddMissingSeasons(series, seasons, allKnownSeasonNumbers)) {
                    seasons.Add(pair.Key, pair.Value);
                }

                // Add missing episodes
                foreach (var episodeInfo in seriesInfo.EpisodeList) {
                    if (existingEpisodes.Contains(episodeInfo.Id))
                        continue;

                    var seasonNumber = episodeInfoToSeasonNumberDirectory[episodeInfo];
                    if (!seasons.TryGetValue(seasonNumber, out var season) || season == null)
                        continue;

                    AddVirtualEpisode(null, seriesInfo, episodeInfo, season);
                }
            }
        }

        private void HandleSeason(Season season, bool deleted = false)
        {
            if (!(season.ProviderIds.TryGetValue("Shoko Series", out var seriesId) || season.Series.ProviderIds.TryGetValue("Shoko Series", out seriesId)) || string.IsNullOrEmpty(seriesId))
                return;

            var seasonNumber = season.IndexNumber!.Value;
            var existingEpisodes = season.Children.OfType<Episode>()
                .Where(ep => ep.ProviderIds.TryGetValue("Shoko Episode", out var episodeId) && !string.IsNullOrEmpty(episodeId))
                .Select(ep => ep.ProviderIds["Shoko Episode"])
                .ToHashSet();
            var series = season.Series;
            Info.GroupInfo groupInfo = null;
            Info.SeriesInfo seriesInfo = null;

            if (Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup) {
                var filterLibrary = Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default;
                groupInfo = ApiManager.GetGroupInfoForSeriesSync(seriesId, filterLibrary);
                if (groupInfo == null) {
                    Logger.LogWarning("Unable to find group info for series. (Series={SeriesId})", seriesId);
                    return;
                }

                int seriesIndex = seasonNumber > 0 ? seasonNumber - 1 : seasonNumber;
                var index = groupInfo.DefaultSeriesIndex + seriesIndex;
                seriesInfo = groupInfo.SeriesList[index];
                if (seriesInfo == null) {
                    Logger.LogWarning("Unable to find series info for {SeasonNumber} in group for series. (Group={GroupId})", seasonNumber, groupInfo.Id);
                    return;
                }

                if (deleted)
                    season = AddVirtualSeason(seriesInfo, seasonNumber, series);
            }
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

        private void HandleEpisode(Episode episode)
        {
            // Abort if we're unable to get the shoko episode id
            if (!episode.ProviderIds.TryGetValue("Shoko Episode", out var episodeId) || string.IsNullOrEmpty(episodeId))
                return;

            Info.GroupInfo groupInfo = null;
            Info.SeriesInfo seriesInfo = ApiManager.GetSeriesInfoForEpisodeSync(episodeId);
            Info.EpisodeInfo episodeInfo = seriesInfo.EpisodeList.Find(e => e.Id == episodeId);
            if (Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup)
                groupInfo = ApiManager.GetGroupInfoForSeriesSync(seriesInfo.Id, Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default);

            AddVirtualEpisode(groupInfo, seriesInfo, episodeInfo, episode.Season);
        }

        private IEnumerable<KeyValuePair<int, Season>> AddMissingSeasons(Series series, Dictionary<int, Season> existingSeasons, List<int> allSeasonNumbers)
        {
            var missingSeasonNumbers = allSeasonNumbers.Except(existingSeasons.Keys).ToList();
            foreach (var seasonNumber in missingSeasonNumbers) {
                yield return KeyValuePair.Create(seasonNumber, AddVirtualSeason(seasonNumber, series));
            }
        }

        private IEnumerable<KeyValuePair<int, Season>> AddMissingSeasons(Info.GroupInfo groupInfo, Series series, Dictionary<int, Season> seasons)
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
                yield return KeyValuePair.Create(seasonNumber, season);
            }
            if (hasSpecials)
                yield return KeyValuePair.Create(0, AddVirtualSeason(0, series));
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
                SeriesPresentationUniqueKey = series.GetPresentationUniqueKey()
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
            };
            result.ProviderIds.Add("Shoko Series", seriesInfo.Id);
            if (Plugin.Instance.Configuration.AddAniDBId)
                result.ProviderIds.Add("AniDB", seriesInfo.AniDB.ID.ToString());

            series.AddChild(result, CancellationToken.None);

            return result;
        }

        private void AddVirtualEpisode(Info.GroupInfo groupInfo, Info.SeriesInfo seriesInfo, Info.EpisodeInfo episodeInfo, MediaBrowser.Controller.Entities.TV.Season season)
        {
            var episodeId = LibraryManager.GetNewItemId(season.Series.Id + "Season " + seriesInfo.Id + " Episode " + episodeInfo.Id, typeof(Episode));
            var result = EpisodeProvider.CreateMetadata(groupInfo, seriesInfo, episodeInfo, season, episodeId);

            Logger.LogInformation("Creating virtual episode {EpisodeName} S{SeasonNumber}:E{EpisodeNumber}", season.Series.Name, season.IndexNumber, result.IndexNumber);

            season.AddChild(result, CancellationToken.None);
        }
    }
}