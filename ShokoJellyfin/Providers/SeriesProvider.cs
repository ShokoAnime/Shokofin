using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using ShokoJellyfin.API;

namespace ShokoJellyfin.Providers
{
    public class SeriesProvider : IHasOrder, IRemoteMetadataProvider<Series, SeriesInfo>
    {
        public string Name => "Shoko";
        public int Order => 1;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<SeriesProvider> _logger;

        public SeriesProvider(IHttpClientFactory httpClientFactory, ILogger<SeriesProvider> logger)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            try
            {
                var result = new MetadataResult<Series>();

                var dirname = Path.DirectorySeparatorChar + info.Path.Split(Path.DirectorySeparatorChar).Last();
                
                _logger.LogInformation($"Shoko Scanner... Getting series ID ({dirname})");
                
                var apiResponse = await ShokoAPI.GetSeriesPathEndsWith(dirname);
                var seriesIDs = apiResponse.FirstOrDefault()?.IDs;
                var seriesId = seriesIDs?.ID.ToString();
                
                if (string.IsNullOrEmpty(seriesId))
                {
                    _logger.LogInformation("Shoko Scanner... Series not found!");
                    return result;
                }
                
                _logger.LogInformation($"Shoko Scanner... Getting series metadata ({dirname} - {seriesId})");

                var seriesInfo = await ShokoAPI.GetSeries(seriesId);
                var aniDbSeriesInfo = await ShokoAPI.GetSeriesAniDb(seriesId);
                var tags = await ShokoAPI.GetSeriesTags(seriesId, GetFlagFilter());
                var ( displayTitle, alternateTitle ) = Helper.GetSeriesTitles(aniDbSeriesInfo.Titles, aniDbSeriesInfo.Title, Plugin.Instance.Configuration.TitleMainType, Plugin.Instance.Configuration.TitleAlternateType, info.MetadataLanguage);
                
                result.Item = new Series
                {
                    Name = displayTitle,
                    OriginalTitle = alternateTitle,
                    Overview = Helper.SummarySanitizer(aniDbSeriesInfo.Description),
                    PremiereDate = aniDbSeriesInfo.AirDate,
                    EndDate = aniDbSeriesInfo.EndDate,
                    ProductionYear = aniDbSeriesInfo.AirDate?.Year,
                    Status = aniDbSeriesInfo.EndDate == null ? SeriesStatus.Continuing : SeriesStatus.Ended,
                    Tags = tags?.Select(tag => tag.Name).ToArray() ?? new string[0],
                    CommunityRating = (float)((aniDbSeriesInfo.Rating.Value * 10) / aniDbSeriesInfo.Rating.MaxValue)
                };
                result.Item.SetProviderId("Shoko Series", seriesId);
                result.Item.SetProviderId("AniDB", seriesIDs.AniDB.ToString());
                var tvdbId = seriesIDs.TvDB?.FirstOrDefault();
                if (tvdbId != 0) result.Item.SetProviderId("Tvdb", tvdbId.ToString());
                result.HasMetadata = true;
                
                result.ResetPeople();
                var roles = await ShokoAPI.GetSeriesCast(seriesId);
                foreach (var role in roles)
                {
                    result.AddPerson(new PersonInfo
                    {
                        Type = PersonType.Actor,
                        Name = role.Staff.Name,
                        Role = role.Character.Name,
                        ImageUrl = Helper.GetImageUrl(role.Staff.Image)
                    });
                }
                
                return result;
            }
            catch (Exception e)
            {
                _logger.LogError(e.StackTrace);
                return new MetadataResult<Series>();
            }
        }
        
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Shoko Scanner... Searching Series ({searchInfo.Name})");
            var searchResults = await ShokoAPI.SeriesSearch(searchInfo.Name);
            var results = new List<RemoteSearchResult>();

            foreach (var series in searchResults)
            {
                var imageUrl = Helper.GetImageUrl(series.Images.Posters.FirstOrDefault());
                _logger.LogInformation(imageUrl);
                var parsedSeries = new RemoteSearchResult
                {
                    Name = series.Name,
                    SearchProviderName = Name,
                    ImageUrl = imageUrl
                };
                parsedSeries.SetProviderId("Shoko", series.IDs.ID.ToString());
                results.Add(parsedSeries);
            }
            
            return results;
        }
        
        
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
        }

        private int GetFlagFilter()
        {
            var config = Plugin.Instance.Configuration;
            var filter = 0;

            if (config.HideAniDbTags) filter = 1;
            if (config.HideArtStyleTags) filter |= (filter << 1);
            if (config.HideSourceTags) filter |= (filter << 2);
            if (config.HideMiscTags) filter |= (filter << 3);
            if (config.HidePlotTags) filter |= (filter << 4);

            return filter;
        }
    }
}