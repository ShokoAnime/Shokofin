using System;
using System.Collections.Generic;
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
                    Logger.LogWarning($"Unable to find file info for path {info.Path}");
                    return result;
                }
                Logger.LogInformation($"Found file info for path {info.Path}");

                string displayTitle, alternateTitle;
                if (series.AniDB.Type == API.Models.SeriesType.Movie)
                    ( displayTitle, alternateTitle ) = Text.GetMovieTitles(series.AniDB.Titles, episode.AniDB.Titles, series.Shoko.Name, episode.Shoko.Name, info.MetadataLanguage);
                else
                    ( displayTitle, alternateTitle ) = Text.GetEpisodeTitles(series.AniDB.Titles, episode.AniDB.Titles, episode.Shoko.Name, info.MetadataLanguage);

                if (group != null && episode.AniDB.Type != EpisodeType.Normal && config.MarkSpecialsWhenGrouped) {
                    displayTitle = $"SP {episode.AniDB.EpisodeNumber} {displayTitle}";
                    alternateTitle = $"SP {episode.AniDB.EpisodeNumber} {alternateTitle}";
                }

                result.Item = new Episode {
                    IndexNumber = Ordering.GetIndexNumber(series, episode),
                    ParentIndexNumber = Ordering.GetSeasonNumber(group, series, episode),
                    Name = displayTitle,
                    OriginalTitle = alternateTitle,
                    PremiereDate = episode.AniDB.AirDate,
                    Overview = Text.SanitizeTextSummary(episode.AniDB.Description),
                    CommunityRating = episode.AniDB.Rating.ToFloat(10),
                };
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
