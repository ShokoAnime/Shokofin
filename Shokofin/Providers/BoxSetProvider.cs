using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.API;

namespace Shokofin.Providers
{
    public class BoxSetProvider : IHasOrder, IRemoteMetadataProvider<BoxSet, BoxSetInfo>
    {
        public string Name => "Shoko";
        public int Order => 1;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<BoxSetProvider> _logger;

        public BoxSetProvider(IHttpClientFactory httpClientFactory, ILogger<BoxSetProvider> logger)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<MetadataResult<BoxSet>> GetMetadata(BoxSetInfo info, CancellationToken cancellationToken)
        {
            try
            {
                var result = new MetadataResult<BoxSet>();

                var dirname = Path.DirectorySeparatorChar + info.Path.Split(Path.DirectorySeparatorChar).Last();

                _logger.LogInformation($"Shoko Scanner... Getting series ID ({dirname})");

                var apiResponse = await ShokoAPI.GetSeriesPathEndsWith(dirname);
                var seriesIDs = apiResponse.FirstOrDefault()?.IDs;
                var seriesId = seriesIDs?.ID.ToString();

                if (string.IsNullOrEmpty(seriesId))
                {
                    _logger.LogInformation("Shoko Scanner... BoxSet not found!");
                    return result;
                }
                _logger.LogInformation($"Shoko Scanner... Getting series metadata ({dirname} - {seriesId})");

                var aniDbInfo = await ShokoAPI.GetSeriesAniDb(seriesId);
                if (aniDbInfo.Type != "Movie")
                {
                    _logger.LogInformation("Shoko Scanner... series was not a movie! Skipping.");
                    return result;
                }

                var seriesInfo = await ShokoAPI.GetSeries(seriesId);
                if (seriesInfo.Sizes.Total.Episodes <= 1)
                {
                    _logger.LogInformation("Shoko Scanner... series did not contain multiple movies! Skipping.");
                    return result;
                }
                var tags = await ShokoAPI.GetSeriesTags(seriesId, Helper.GetTagFilter());

                var ( displayTitle, alternateTitle ) = Helper.GetSeriesTitles(aniDbInfo.Titles, aniDbInfo.Title, Plugin.Instance.Configuration.TitleMainType, Plugin.Instance.Configuration.TitleAlternateType, info.MetadataLanguage);
                result.Item = new BoxSet
                {
                    Name = displayTitle,
                    OriginalTitle = alternateTitle,
                    Overview = Helper.SummarySanitizer(aniDbInfo.Description),
                    PremiereDate = aniDbInfo.AirDate,
                    EndDate = aniDbInfo.EndDate,
                    ProductionYear = aniDbInfo.AirDate?.Year,
                    Tags = tags?.Select(tag => tag.Name).ToArray() ?? new string[0],
                    CommunityRating = (float)((aniDbInfo.Rating.Value * 10) / aniDbInfo.Rating.MaxValue)
                };
                result.Item.SetProviderId("Shoko Series", seriesId);
                result.HasMetadata = true;

                return result;
            }
            catch (Exception e)
            {
                _logger.LogError(e.StackTrace);
                return new MetadataResult<BoxSet>();
            }
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BoxSetInfo searchInfo, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Shoko Scanner... Searching BoxSet ({searchInfo.Name})");
            var searchResults = await ShokoAPI.SeriesSearch(searchInfo.Name);
            var results = new List<RemoteSearchResult>();

            foreach (var series in searchResults)
            {
                var imageUrl = Helper.GetImageUrl(series.Images.Posters.FirstOrDefault());
                _logger.LogInformation(imageUrl);
                var parsedBoxSet = new RemoteSearchResult
                {
                    Name = series.Name,
                    SearchProviderName = Name,
                    ImageUrl = imageUrl
                };
                parsedBoxSet.SetProviderId("Shoko", series.IDs.ID.ToString());
                results.Add(parsedBoxSet);
            }

            return results;
        }


        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
        }
    }
}