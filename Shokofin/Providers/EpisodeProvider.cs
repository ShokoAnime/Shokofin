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
using Shokofin.Utils;

namespace Shokofin.Providers
{
    public class EpisodeProvider: IRemoteMetadataProvider<Episode, EpisodeInfo>
    {
        public string Name => "Shoko";
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<EpisodeProvider> _logger;

        public EpisodeProvider(IHttpClientFactory httpClientFactory, ILogger<EpisodeProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            try
            {
                var result = new MetadataResult<Episode>();

                var includeGroup = Plugin.Instance.Configuration.SeriesGrouping == OrderingUtil.SeriesGroupType.ShokoGroup;
                var (id, file, episode, series, group) = await DataUtil.GetFileInfoByPath(info.Path, includeGroup);

                if (file == null) // if file is null then series and episode is also null.
                {
                    _logger.LogWarning($"Unable to find file info for path {id}");
                    return result;
                }
                _logger.LogInformation($"Found file info for path {id}");

                var extraType = OrderingUtil.GetExtraType(episode.AniDB);
                if (extraType != null)
                {
                    _logger.LogDebug($"Not a normal or special episode, skipping path {id}");
                    result.HasMetadata = false;
                    return result;
                }
                _logger.LogInformation($"Getting episode metadata ({info.Path} - {episode.ID})");

                var ( displayTitle, alternateTitle ) = TextUtil.GetEpisodeTitles(series.AniDB.Titles, episode.AniDB.Titles, episode.Shoko.Name, info.MetadataLanguage);
                int aniDBId = episode.AniDB.ID;
                int tvdbId = episode?.TvDB?.ID ?? 0;

                result.Item = new Episode
                {
                    IndexNumber = OrderingUtil.GetIndexNumber(series, episode),
                    ParentIndexNumber = OrderingUtil.GetSeasonNumber(group, series, episode),
                    Name = displayTitle,
                    OriginalTitle = alternateTitle,
                    PremiereDate = episode.AniDB.AirDate,
                    Overview = TextUtil.SummarySanitizer(episode.AniDB.Description),
                    CommunityRating = (float) ((episode.AniDB.Rating.Value * 10) / episode.AniDB.Rating.MaxValue)
                };
                result.Item.SetProviderId("Shoko Episode", episode.ID);
                result.Item.SetProviderId("Shoko File", file.ID);
                result.Item.SetProviderId("AniDB", aniDBId.ToString());
                if (tvdbId != 0) result.Item.SetProviderId("Tvdb", tvdbId.ToString());
                result.HasMetadata = true;

                var episodeNumberEnd = episode.AniDB.EpisodeNumber + episode.OtherEpisodesCount;
                if (episode.AniDB.EpisodeNumber != episodeNumberEnd) result.Item.IndexNumberEnd = episodeNumberEnd;

                return result;
            }
            catch (Exception e)
            {
                _logger.LogError($"{e.Message}{Environment.NewLine}{e.StackTrace}");
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
            return _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
        }
    }
}
