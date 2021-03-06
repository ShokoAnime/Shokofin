using System;
using System.Collections.Generic;
using System.IO;
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
                
                // TO-DO Check if it can be written in a better way. Parent directory + File Name
                var filename = Path.Join(
                    Path.GetDirectoryName(info.Path)?.Split(Path.DirectorySeparatorChar).LastOrDefault(),
                    Path.GetFileName(info.Path));

                _logger.LogInformation($"Getting episode ID ({filename})");

                var apiResponse = await ShokoAPI.GetFilePathEndsWith(filename);
                var file = apiResponse.FirstOrDefault();
                var fileId = file?.ID.ToString();
                var series = file?.SeriesIDs.FirstOrDefault();
                var seriesId = series?.SeriesID.ID.ToString();
                var episodeIDs = series?.EpisodeIDs?.FirstOrDefault();
                var episodeId = episodeIDs?.ID.ToString();

                if (string.IsNullOrEmpty(fileId) || string.IsNullOrEmpty(seriesId) || string.IsNullOrEmpty(episodeId))
                {
                    _logger.LogInformation($"Episode not found! ({filename})");
                    return result;
                }

                _logger.LogInformation($"Getting episode metadata ({filename} - {episodeId})");

                var seriesInfo = await ShokoAPI.GetSeriesAniDb(seriesId);
                var episode = await ShokoAPI.GetEpisode(episodeId);
                var episodeInfo = await ShokoAPI.GetEpisodeAniDb(episodeId);
                var ( displayTitle, alternateTitle ) = Helper.GetEpisodeTitles(seriesInfo.Titles, episodeInfo.Titles, episode.Name, Plugin.Instance.Configuration.TitleMainType, Plugin.Instance.Configuration.TitleAlternateType, info.MetadataLanguage);

                result.Item = new Episode
                {
                    IndexNumber = episodeInfo.EpisodeNumber,
                    ParentIndexNumber = await GetSeasonNumber(episodeId, episodeInfo.Type),
                    Name = displayTitle,
                    OriginalTitle = alternateTitle,
                    PremiereDate = episodeInfo.AirDate,
                    Overview = Helper.SummarySanitizer(episodeInfo.Description),
                    CommunityRating = (float) ((episodeInfo.Rating.Value * 10) / episodeInfo.Rating.MaxValue)
                };
                result.Item.SetProviderId("Shoko Episode", episodeId);
                result.Item.SetProviderId("Shoko File", fileId);
                result.Item.SetProviderId("AniDB", episodeIDs.AniDB.ToString());
                result.HasMetadata = true;

                var episodeNumberEnd = episodeInfo.EpisodeNumber + series?.EpisodeIDs.Count() - 1;
                if (episodeInfo.EpisodeNumber != episodeNumberEnd) result.Item.IndexNumberEnd = episodeNumberEnd;

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

        private async Task<int> GetSeasonNumber(string episodeId, string type)
        {
            var seasonNumber = 0;
            
            switch (type)
            {
                case "Normal":
                    seasonNumber = 1;
                    break;
                case "ThemeSong":
                    seasonNumber = 100;
                    break;
                case "Special":
                    seasonNumber = 0;
                    break;
                case "Trailer":
                    seasonNumber = 99;
                    break;
                default:
                    seasonNumber = 98;
                    break;
            }

            if (Plugin.Instance.Configuration.UseTvDbSeasonOrdering && seasonNumber < 98)
            {
                var tvdbEpisodeInfo = await ShokoAPI.GetEpisodeTvDb(episodeId);
                var tvdbSeason = tvdbEpisodeInfo.FirstOrDefault()?.Season;
                return tvdbSeason ?? seasonNumber;
            }

            return seasonNumber;
        }
    }
}
