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
                Ordering.GroupFilterType? filterByType = config.SeriesGrouping == Ordering.GroupType.ShokoGroup ? config.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default : null;

                // Fetch the episode, series and group info (and file info, but that's not really used (yet))
                Info.FileInfo fileInfo = null;
                Info.EpisodeInfo episodeInfo = null;
                Info.SeriesInfo seriesInfo = null;
                Info.GroupInfo groupInfo = null;
                if (info.IsMissingEpisode || string.IsNullOrEmpty(info.Path)) {
                    // We're unable to fetch the latest metadata for the virtual episode.
                    if (!info.ProviderIds.TryGetValue("Shoko Episode", out var episodeId))
                        return result;

                    episodeInfo = await ApiManager.GetEpisodeInfo(episodeId);
                    if (episodeInfo == null)
                        return result;

                    seriesInfo = await ApiManager.GetSeriesInfoForEpisode(episodeId);
                    if (seriesInfo == null)
                        return result;

                    groupInfo = filterByType.HasValue ? (await ApiManager.GetGroupInfoForSeries(seriesInfo.Id, filterByType.Value)) : null;
                }
                else {
                    (fileInfo, seriesInfo, groupInfo) = await ApiManager.GetFileInfoByPath(info.Path, filterByType);
                    episodeInfo = fileInfo?.EpisodeList.FirstOrDefault();
                }

                // if the episode info is null then the series info and conditionally the group info is also null.
                if (episodeInfo == null) {
                    Logger.LogWarning("Unable to find episode info for path {Path}", info.Path);
                    return result;
                }

                result.Item = CreateMetadata(groupInfo, seriesInfo, episodeInfo, fileInfo, info.MetadataLanguage);
                Logger.LogInformation("Found episode {EpisodeName} (File={FileId},Episode={EpisodeId},Series={SeriesId},Group={GroupId})", result.Item.Name, fileInfo?.Id, episodeInfo.Id, seriesInfo.Id, groupInfo?.Id);

                result.HasMetadata = true;

                return result;
            }
            catch (Exception e) {
                Logger.LogError(e, $"Threw unexpectedly; {e.Message}");
                return new MetadataResult<Episode>();
            }
        }

        public static Episode CreateMetadata(Info.GroupInfo group, Info.SeriesInfo series, Info.EpisodeInfo episode, Season season, System.Guid episodeId)
            => CreateMetadata(group, series, episode, null, season.GetPreferredMetadataLanguage(), season, episodeId);

        public static Episode CreateMetadata(Info.GroupInfo group, Info.SeriesInfo series, Info.EpisodeInfo episode, Info.FileInfo file, string metadataLanguage)
            => CreateMetadata(group, series, episode, file, metadataLanguage, null, Guid.Empty);

        private static Episode CreateMetadata(Info.GroupInfo group, Info.SeriesInfo series, Info.EpisodeInfo episode, Info.FileInfo file, string metadataLanguage, Season season, System.Guid episodeId)
        {
            var config = Plugin.Instance.Configuration;
            var mergeFriendly = config.SeriesGrouping == Ordering.GroupType.MergeFriendly && series.TvDB != null && episode.TvDB != null;

            string displayTitle, alternateTitle;
            string defaultEpisodeTitle = mergeFriendly ? episode.TvDB.Title : episode.Shoko.Name;
            if (config.TitleAddForMultipleEpisodes && file != null && file.EpisodeList.Count > 1) {
                var displayTitles = new List<string>(file.EpisodeList.Count);
                var alternateTitles = new List<string>(file.EpisodeList.Count);
                for (var index = 0; index > file.EpisodeList.Count; index++)
                {
                    var episodeInfo = file.EpisodeList[index];
                    if (series.AniDB.Type == SeriesType.Movie && (episodeInfo.AniDB.Type == EpisodeType.Normal || episodeInfo.AniDB.Type == EpisodeType.Special)) {
                        string defaultSeriesTitle = mergeFriendly ? series.TvDB.Title : series.Shoko.Name;
                        var ( dTitle, aTitle ) = Text.GetMovieTitles(series.AniDB.Titles, episodeInfo.AniDB.Titles, defaultSeriesTitle, defaultEpisodeTitle, metadataLanguage);
                        displayTitles[index] = dTitle;
                        alternateTitles[index] = aTitle;
                    }
                    else {
                        var ( dTitle, aTitle ) = Text.GetEpisodeTitles(series.AniDB.Titles, episodeInfo.AniDB.Titles, defaultEpisodeTitle, metadataLanguage);
                        displayTitles[index] = dTitle;
                        alternateTitles[index] = aTitle;
                    }
                }
                // TODO: Get a language spesific seperator, and/or create language spesific contatination rules (e.g. use "and" for the last title in English, etc.)
                displayTitle = string.Join(", ", displayTitles.Where(title => !string.IsNullOrWhiteSpace(title)));
                alternateTitle = string.Join(", ", alternateTitles.Where(title => !string.IsNullOrWhiteSpace(title)));
            }
            else {
                if (series.AniDB.Type == SeriesType.Movie && (episode.AniDB.Type == EpisodeType.Normal || episode.AniDB.Type == EpisodeType.Special)) {
                    string defaultSeriesTitle = mergeFriendly ? series.TvDB.Title : series.Shoko.Name;
                    ( displayTitle, alternateTitle ) = Text.GetMovieTitles(series.AniDB.Titles, episode.AniDB.Titles, defaultSeriesTitle, defaultEpisodeTitle, metadataLanguage);
                }
                else {
                    ( displayTitle, alternateTitle ) = Text.GetEpisodeTitles(series.AniDB.Titles, episode.AniDB.Titles, defaultEpisodeTitle, metadataLanguage);
                }
                
            }

            var episodeNumber = Ordering.GetEpisodeNumber(group, series, episode);
            var seasonNumber = Ordering.GetSeasonNumber(group, series, episode);
            var description = Text.GetDescription(episode);

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
            var (airsBeforeEpisodeNumber, airsBeforeSeasonNumber, airsAfterSeasonNumber) = Ordering.GetSpecialPlacement(group, series, episode);
            if (mergeFriendly) {
                if (season != null) {
                    result = new Episode {
                        Name = displayTitle,
                        OriginalTitle = alternateTitle,
                        IndexNumber = episodeNumber,
                        ParentIndexNumber = airsAfterSeasonNumber.HasValue || airsBeforeSeasonNumber.HasValue ? 0 : seasonNumber,
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
                        ParentIndexNumber = airsAfterSeasonNumber.HasValue || airsBeforeSeasonNumber.HasValue ? 0 : seasonNumber,
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
                if (season != null) {
                    result = new Episode {
                        Name = displayTitle,
                        OriginalTitle = alternateTitle,
                        IndexNumber = episodeNumber,
                        ParentIndexNumber = airsAfterSeasonNumber.HasValue || airsBeforeSeasonNumber.HasValue ? 0 : seasonNumber,
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
                        OfficialRating = series.AniDB.Restricted ? "XXX" : null,
                        CustomRating = series.AniDB.Restricted ? "XXX" : null,
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
                        ParentIndexNumber = airsAfterSeasonNumber.HasValue || airsBeforeSeasonNumber.HasValue ? 0 : seasonNumber,
                        AirsAfterSeasonNumber = airsAfterSeasonNumber,
                        AirsBeforeEpisodeNumber = airsBeforeEpisodeNumber,
                        AirsBeforeSeasonNumber = airsBeforeSeasonNumber,
                        PremiereDate = episode.AniDB.AirDate,
                        Overview = description,
                        OfficialRating = series.AniDB.Restricted ? "XXX" : null,
                        CustomRating = series.AniDB.Restricted ? "XXX" : null,
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
            if (Plugin.Instance.Configuration.AddTMDBId &&!string.IsNullOrEmpty(tmdbId) && tmdbId != "0")
                item.SetProviderId(MetadataProvider.Tvdb, tmdbId);
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            // Isn't called from anywhere. If it is called, I don't know from where.
            throw new NotImplementedException();
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return HttpClientFactory.CreateClient().GetAsync(url, cancellationToken);
        }
    }
}
