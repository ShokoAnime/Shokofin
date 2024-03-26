using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Utils;

using Info = Shokofin.API.Info;
using SeriesType = Shokofin.API.Models.SeriesType;
using EpisodeType = Shokofin.API.Models.EpisodeType;

namespace Shokofin.Providers
{
    public class EpisodeProvider: IRemoteMetadataProvider<Episode, EpisodeInfo>
    {
        public string Name => Plugin.MetadataProviderName;

        private readonly IHttpClientFactory HttpClientFactory;

        private readonly ILogger<EpisodeProvider> Logger;

        private readonly ShokoAPIManager ApiManager;

        public EpisodeProvider(IHttpClientFactory httpClientFactory, ILogger<EpisodeProvider> logger, ShokoAPIManager apiManager)
        {
            HttpClientFactory = httpClientFactory;
            Logger = logger;
            ApiManager = apiManager;
        }

        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            try {
                var result = new MetadataResult<Episode>();
                var config = Plugin.Instance.Configuration;
                var filterByType = config.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default;

                // Fetch the episode, series and group info (and file info, but that's not really used (yet))
                Info.FileInfo fileInfo = null;
                Info.EpisodeInfo episodeInfo = null;
                Info.SeasonInfo seasonInfo = null;
                Info.ShowInfo showInfo = null;
                if (info.IsMissingEpisode || string.IsNullOrEmpty(info.Path)) {
                    // We're unable to fetch the latest metadata for the virtual episode.
                    if (!info.ProviderIds.TryGetValue("Shoko Episode", out var episodeId))
                        return result;

                    episodeInfo = await ApiManager.GetEpisodeInfo(episodeId);
                    if (episodeInfo == null)
                        return result;

                    seasonInfo = await ApiManager.GetSeasonInfoForEpisode(episodeId);
                    if (seasonInfo == null)
                        return result;

                    showInfo = await ApiManager.GetShowInfoForSeries(seasonInfo.Id, filterByType);
                    if (showInfo == null || showInfo.SeasonList.Count == 0)
                        return result;
                }
                else {
                    (fileInfo, seasonInfo, showInfo) = await ApiManager.GetFileInfoByPath(info.Path, filterByType);
                    episodeInfo = fileInfo?.EpisodeList.FirstOrDefault();
                }

                // if the episode info is null then the series info and conditionally the group info is also null.
                if (episodeInfo == null) {
                    Logger.LogWarning("Unable to find episode info for path {Path}", info.Path);
                    return result;
                }

                result.Item = CreateMetadata(showInfo, seasonInfo, episodeInfo, fileInfo, info.MetadataLanguage);
                Logger.LogInformation("Found episode {EpisodeName} (File={FileId},Episode={EpisodeId},Series={SeriesId},Group={GroupId})", result.Item.Name, fileInfo?.Id, episodeInfo.Id, seasonInfo.Id, showInfo?.Id);

                result.HasMetadata = true;

                return result;
            }
            catch (Exception ex) {
                Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
                return new MetadataResult<Episode>();
            }
        }

        public static Episode CreateMetadata(Info.ShowInfo group, Info.SeasonInfo series, Info.EpisodeInfo episode, Season season, System.Guid episodeId)
            => CreateMetadata(group, series, episode, null, season.GetPreferredMetadataLanguage(), season, episodeId);

        public static Episode CreateMetadata(Info.ShowInfo group, Info.SeasonInfo series, Info.EpisodeInfo episode, Info.FileInfo file, string metadataLanguage)
            => CreateMetadata(group, series, episode, file, metadataLanguage, null, Guid.Empty);

        private static Episode CreateMetadata(Info.ShowInfo group, Info.SeasonInfo series, Info.EpisodeInfo episode, Info.FileInfo file, string metadataLanguage, Season season, System.Guid episodeId)
        {
            var config = Plugin.Instance.Configuration;
            var maybeMergeFriendly = config.SeriesGrouping == Ordering.GroupType.MergeFriendly && series.TvDB != null;
            var mergeFriendly = maybeMergeFriendly && episode.TvDB != null;

            string displayTitle, alternateTitle, description;
            if (config.TitleAddForMultipleEpisodes && file != null && file.EpisodeList.Count > 1) {
                var displayTitles = new List<string>(file.EpisodeList.Count);
                var alternateTitles = new List<string>(file.EpisodeList.Count);
                foreach (var episodeInfo in file.EpisodeList)
                {
                    string defaultEpisodeTitle = episodeInfo.Shoko.Name;
                    if (
                        // Movies
                        (series.AniDB.Type == SeriesType.Movie && (episodeInfo.AniDB.Type == EpisodeType.Normal || episodeInfo.AniDB.Type == EpisodeType.Special)) ||
                        // OVAs
                        (series.AniDB.Type == SeriesType.OVA && episodeInfo.AniDB.Type == EpisodeType.Normal && episodeInfo.AniDB.EpisodeNumber == 1 && episodeInfo.Shoko.Name == "OVA")
                    ) {
                        string defaultSeriesTitle = series.Shoko.Name;
                        var ( dTitle, aTitle ) = Text.GetMovieTitles(series.AniDB.Titles, episodeInfo.AniDB.Titles, defaultSeriesTitle, defaultEpisodeTitle, metadataLanguage);
                        displayTitles.Add(dTitle);
                        alternateTitles.Add(aTitle);
                    }
                    else {
                        var ( dTitle, aTitle ) = Text.GetEpisodeTitles(series.AniDB.Titles, episodeInfo.AniDB.Titles, defaultEpisodeTitle, metadataLanguage);
                        displayTitles.Add(dTitle);
                        alternateTitles.Add(aTitle);
                    }
                }
                displayTitle = Text.JoinText(displayTitles);
                alternateTitle = Text.JoinText(alternateTitles);
                description = Text.GetDescription(file.EpisodeList);
            }
            else {
                string defaultEpisodeTitle = mergeFriendly ? episode.TvDB.Title : episode.Shoko.Name;
                if (series.AniDB.Type == SeriesType.Movie && (episode.AniDB.Type == EpisodeType.Normal || episode.AniDB.Type == EpisodeType.Special)) {
                    string defaultSeriesTitle = mergeFriendly ? series.TvDB.Title : series.Shoko.Name;
                    ( displayTitle, alternateTitle ) = Text.GetMovieTitles(series.AniDB.Titles, episode.AniDB.Titles, defaultSeriesTitle, defaultEpisodeTitle, metadataLanguage);
                }
                else {
                    ( displayTitle, alternateTitle ) = Text.GetEpisodeTitles(series.AniDB.Titles, episode.AniDB.Titles, defaultEpisodeTitle, metadataLanguage);
                }
                description = Text.GetDescription(episode);
            }

            var episodeNumber = Ordering.GetEpisodeNumber(group, series, episode);
            var seasonNumber = Ordering.GetSeasonNumber(group, series, episode);

            if (config.MarkSpecialsWhenGrouped) switch (episode.AniDB.Type) {
                case EpisodeType.Unknown:
                case EpisodeType.Other:
                case EpisodeType.Normal:
                    break;
                case EpisodeType.Special: {
                    // We're guaranteed to find the index, because otherwise it would've thrown when getting the episode number.
                    var index = series.SpecialsList.FindIndex(ep => ep == episode);
                    displayTitle = $"S{index + 1} {displayTitle}";
                    alternateTitle = $"S{index + 1} {alternateTitle}";
                    break;
                }
                case EpisodeType.ThemeSong:
                case EpisodeType.EndingSong:
                case EpisodeType.OpeningSong:
                    displayTitle = $"C{episode.AniDB.EpisodeNumber} {displayTitle}";
                    alternateTitle = $"C{episode.AniDB.EpisodeNumber} {alternateTitle}";
                    break;
                case EpisodeType.Trailer:
                    displayTitle = $"T{episode.AniDB.EpisodeNumber} {displayTitle}";
                    alternateTitle = $"T{episode.AniDB.EpisodeNumber} {alternateTitle}";
                    break;
                case EpisodeType.Parody:
                    displayTitle = $"P{episode.AniDB.EpisodeNumber} {displayTitle}";
                    alternateTitle = $"P{episode.AniDB.EpisodeNumber} {alternateTitle}";
                    break;
                default:
                    displayTitle = $"U{episode.AniDB.EpisodeNumber} {displayTitle}";
                    alternateTitle = $"U{episode.AniDB.EpisodeNumber} {alternateTitle}";
                    break;
            }

            Episode result;
            var (airsBeforeEpisodeNumber, airsBeforeSeasonNumber, airsAfterSeasonNumber, isSpecial) = Ordering.GetSpecialPlacement(group, series, episode);
            if (mergeFriendly) {
                if (season != null) {
                    result = new Episode {
                        Name = displayTitle,
                        OriginalTitle = alternateTitle,
                        IndexNumber = episodeNumber,
                        ParentIndexNumber = isSpecial ? 0 : seasonNumber,
                        AirsAfterSeasonNumber = airsAfterSeasonNumber,
                        AirsBeforeEpisodeNumber = airsBeforeEpisodeNumber,
                        AirsBeforeSeasonNumber = airsBeforeSeasonNumber,
                        Id = episodeId,
                        IsVirtualItem = true,
                        SeasonId = season.Id,
                        SeriesId = season.Series.Id,
                        Overview = description,
                        CommunityRating = episode.TvDB.Rating?.ToFloat(10),
                        PremiereDate = episode.TvDB.AirDate,
                        SeriesName = season.Series.Name,
                        SeriesPresentationUniqueKey = season.SeriesPresentationUniqueKey,
                        SeasonName = season.Name,
                        DateLastSaved = DateTime.UtcNow,
                        RunTimeTicks = episode.AniDB.Duration.Ticks,
                    };
                    result.PresentationUniqueKey = result.GetPresentationUniqueKey();
                }
                else {
                    result = new Episode {
                        Name = displayTitle,
                        OriginalTitle = alternateTitle,
                        IndexNumber = episodeNumber,
                        ParentIndexNumber = isSpecial ? 0 : seasonNumber,
                        AirsAfterSeasonNumber = airsAfterSeasonNumber,
                        AirsBeforeEpisodeNumber = airsBeforeEpisodeNumber,
                        AirsBeforeSeasonNumber = airsBeforeSeasonNumber,
                        CommunityRating = episode.TvDB.Rating?.ToFloat(10),
                        PremiereDate = episode.TvDB.AirDate,
                        Overview = description,
                    };
                }
            }
            else {
                var rating = series.AniDB.Restricted && series.AniDB.Type != SeriesType.TV ? "XXX" : null;
                if (season != null) {
                    result = new Episode {
                        Name = displayTitle,
                        OriginalTitle = alternateTitle,
                        IndexNumber = episodeNumber,
                        ParentIndexNumber = isSpecial ? 0 : seasonNumber,
                        AirsAfterSeasonNumber = airsAfterSeasonNumber,
                        AirsBeforeEpisodeNumber = airsBeforeEpisodeNumber,
                        AirsBeforeSeasonNumber = airsBeforeSeasonNumber,
                        Id = episodeId,
                        IsVirtualItem = true,
                        SeasonId = season.Id,
                        SeriesId = season.Series.Id,
                        Overview = description,
                        CommunityRating = episode.AniDB.Rating.ToFloat(10),
                        PremiereDate = episode.AniDB.AirDate,
                        SeriesName = season.Series.Name,
                        SeriesPresentationUniqueKey = season.SeriesPresentationUniqueKey,
                        SeasonName = season.Name,
                        OfficialRating = rating,
                        CustomRating = rating,
                        DateLastSaved = DateTime.UtcNow,
                        RunTimeTicks = episode.AniDB.Duration.Ticks,
                    };
                    result.PresentationUniqueKey = result.GetPresentationUniqueKey();
                }
                else {
                    result = new Episode {
                        Name = displayTitle,
                        OriginalTitle = alternateTitle,
                        IndexNumber = episodeNumber,
                        ParentIndexNumber = isSpecial ? 0 : seasonNumber,
                        AirsAfterSeasonNumber = airsAfterSeasonNumber,
                        AirsBeforeEpisodeNumber = airsBeforeEpisodeNumber,
                        AirsBeforeSeasonNumber = airsBeforeSeasonNumber,
                        PremiereDate = episode.AniDB.AirDate,
                        Overview = description,
                        OfficialRating = rating,
                        CustomRating = rating,
                        CommunityRating = episode.AniDB.Rating.ToFloat(10),
                    };
                }
            }

            if (file != null) {
                var episodeNumberEnd = episodeNumber + file.EpisodeList.Count - 1;
                if (episode.AniDB.EpisodeNumber != episodeNumberEnd)
                    result.IndexNumberEnd = episodeNumberEnd;
            }

            AddProviderIds(result, episodeId: episode.Id, fileId: file?.Id, anidbId: episode.AniDB.Id.ToString(), tvdbId: mergeFriendly || config.SeriesGrouping == Ordering.GroupType.Default ? episode.TvDB?.Id.ToString() : null);

            return result;
        }

        private static void AddProviderIds(IHasProviderIds item, string episodeId, string fileId = null, string anidbId = null, string tvdbId = null, string tmdbId = null)
        {
            var config = Plugin.Instance.Configuration;
            item.SetProviderId("Shoko Episode", episodeId);
            if (!string.IsNullOrEmpty(fileId))
                item.SetProviderId("Shoko File", fileId);
            if (config.AddAniDBId && !string.IsNullOrEmpty(anidbId) && anidbId != "0")
                item.SetProviderId("AniDB", anidbId);
            if (config.AddTvDBId && !string.IsNullOrEmpty(tvdbId) && tvdbId != "0")
                item.SetProviderId(MetadataProvider.Tvdb, tvdbId);
            if (config.AddTMDBId &&!string.IsNullOrEmpty(tmdbId) && tmdbId != "0")
                item.SetProviderId(MetadataProvider.Tmdb, tmdbId);
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<RemoteSearchResult>>(new List<RemoteSearchResult>());

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
            => HttpClientFactory.CreateClient().GetAsync(url, cancellationToken);
    }
}
