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
                var (file, episode, series, group) = await ApiManager.GetFileInfoByPath(info.Path, filterByType);

                // if file is null then series and episode is also null.
                if (file == null) {
                    Logger.LogWarning("Unable to find episode info for path {Path}", info.Path);
                    return result;
                }

                string displayTitle, alternateTitle;
                if (series.AniDB.Type == API.Models.SeriesType.Movie)
                    ( displayTitle, alternateTitle ) = Text.GetMovieTitles(series.AniDB.Titles, episode.AniDB.Titles, series.Shoko.Name, episode.Shoko.Name, info.MetadataLanguage);
                else
                    ( displayTitle, alternateTitle ) = Text.GetEpisodeTitles(series.AniDB.Titles, episode.AniDB.Titles, episode.Shoko.Name, info.MetadataLanguage);
                Logger.LogInformation("Found episode {EpisodeName} (File={FileId},Episode={EpisodeId},Series={SeriesId})", displayTitle, file.Id, episode.Id, series.Id);

                var episodeNumber = Ordering.GetEpisodeNumber(group, series, episode);
                var seasonNumber = Ordering.GetSeasonNumber(group, series, episode);
                var description = Text.GetDescription(episode);

                if (group != null && config.MarkSpecialsWhenGrouped && episode.AniDB.Type != EpisodeType.Normal) switch (episode.AniDB.Type) {
                    case EpisodeType.Special:
                        displayTitle = $"S{episodeNumber} {displayTitle}";
                        alternateTitle = $"S{episodeNumber} {alternateTitle}";
                        break;
                    case EpisodeType.ThemeSong:
                    case EpisodeType.EndingSong:
                    case EpisodeType.OpeningSong:
                        displayTitle = $"C{episodeNumber} {displayTitle}";
                        alternateTitle = $"C{episodeNumber} {alternateTitle}";
                        break;
                    case EpisodeType.Trailer:
                        displayTitle = $"T{episodeNumber} {displayTitle}";
                        alternateTitle = $"T{episodeNumber} {alternateTitle}";
                        break;
                    case EpisodeType.Parody:
                        displayTitle = $"P{episodeNumber} {displayTitle}";
                        alternateTitle = $"P{episodeNumber} {alternateTitle}";
                        break;
                    case EpisodeType.Other:
                        displayTitle = $"O{episodeNumber} {displayTitle}";
                        alternateTitle = $"O{episodeNumber} {alternateTitle}";
                        break;
                    default:
                        displayTitle = $"U{episodeNumber} {displayTitle}";
                        alternateTitle = $"U{episodeNumber} {alternateTitle}";
                        break;
                }

                if (group != null && episode.AniDB.Type == EpisodeType.Special) {
                    int previousEpisodeNumber;
                    if (!series.SpesialsAnchors.TryGetValue(episode.Id, out var previousEpisode))
                        previousEpisodeNumber = Ordering.GetEpisodeNumber(group, series, episode);
                    else
                        previousEpisodeNumber = series.EpisodeCount;
                    result.Item = new Episode {
                        IndexNumber = episodeNumber,
                        ParentIndexNumber = 0,
                        AirsAfterSeasonNumber = seasonNumber,
                        AirsBeforeEpisodeNumber = previousEpisodeNumber + 1,
                        AirsBeforeSeasonNumber = seasonNumber + 1,
                        Name = displayTitle,
                        OriginalTitle = alternateTitle,
                        PremiereDate = episode.AniDB.AirDate,
                        Overview = description,
                        CommunityRating = episode.AniDB.Rating.ToFloat(10),
                    };
                }
                else {
                    result.Item = new Episode {
                        IndexNumber = episodeNumber,
                        ParentIndexNumber = seasonNumber,
                        Name = displayTitle,
                        OriginalTitle = alternateTitle,
                        PremiereDate = episode.AniDB.AirDate,
                        Overview = description,
                        CommunityRating = episode.AniDB.Rating.ToFloat(10),
                    };
                }
                // NOTE: This next line will remain here till they fix the series merging for providers outside the MetadataProvider enum.
                if (config.SeriesGrouping == Ordering.GroupType.ShokoGroup)
                    result.Item.SetProviderId(MetadataProvider.Imdb, $"INVALID-BUT-DO-NOT-TOUCH:{episode.Id}");
                result.Item.SetProviderId("Shoko Episode", episode.Id);
                result.Item.SetProviderId("Shoko File", file.Id);
                if (config.AddAniDBId)
                    result.Item.SetProviderId("AniDB", episode.AniDB.ID.ToString());
                if (config.AddTvDBId && episode.TvDB != null && config.SeriesGrouping != Ordering.GroupType.ShokoGroup)
                    result.Item.SetProviderId(MetadataProvider.Tvdb, episode.TvDB.ID.ToString());

                result.HasMetadata = true;
                ApiManager.MarkEpisodeAsFound(episode.Id, series.Id);

                var episodeNumberEnd = episode.AniDB.EpisodeNumber + file.EpisodesCount;
                if (episode.AniDB.EpisodeNumber != episodeNumberEnd)
                    result.Item.IndexNumberEnd = episodeNumberEnd;

                return result;
            }
            catch (Exception e) {
                Logger.LogError(e, $"Threw unexpectedly; {e.Message}");
                return new MetadataResult<Episode>();
            }
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
