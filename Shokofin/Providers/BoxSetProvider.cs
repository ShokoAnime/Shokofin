using System;
using System.Collections.Generic;
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
using Shokofin.Utils;

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
                var (id, series) = await DataUtil.GetSeriesInfoByPath(info.Path);

                if (series == null)
                {
                    _logger.LogWarning($"Unable to find series info for path {info.Path}");
                    return result;
                }
                _logger.LogInformation($"Getting series metadata ({info.Path} - {series.ID})");

                int aniDBId = series.AniDB.ID;
                var tvdbId = series?.TvDBID;

                if (series.AniDB.Type != "Movie")
                {
                    _logger.LogWarning("Series found, but not a movie! Skipping.");
                    return result;
                }

                if (series.Shoko.Sizes.Total.Episodes <= 1)
                {
                    _logger.LogWarning("Series did not contain multiple movies! Skipping.");
                    return result;
                }

                var ( displayTitle, alternateTitle ) = TextUtil.GetSeriesTitles(series.AniDB.Titles, series.AniDB.Title, info.MetadataLanguage);
                var tags = await DataUtil.GetTags(series.ID);

                result.Item = new BoxSet
                {
                    Name = displayTitle,
                    OriginalTitle = alternateTitle,
                    Overview = TextUtil.SummarySanitizer(series.AniDB.Description),
                    PremiereDate = series.AniDB.AirDate,
                    EndDate = series.AniDB.EndDate,
                    ProductionYear = series.AniDB.AirDate?.Year,
                    Tags = tags,
                    CommunityRating = (float)((series.AniDB.Rating.Value * 10) / series.AniDB.Rating.MaxValue)
                };
                result.Item.SetProviderId("Shoko Series", series.ID);
                result.HasMetadata = true;

                return result;
            }
            catch (Exception e)
            {
                _logger.LogError($"{e.Message}{Environment.NewLine}{e.StackTrace}");
                return new MetadataResult<BoxSet>();
            }
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BoxSetInfo searchInfo, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Searching BoxSet ({searchInfo.Name})");
            var searchResults = await ShokoAPI.SeriesSearch(searchInfo.Name);

            if (searchResults.Count() == 0) searchResults = await ShokoAPI.SeriesStartsWith(searchInfo.Name);

            var results = new List<RemoteSearchResult>();

            foreach (var series in searchResults)
            {
                var imageUrl = DataUtil.GetImageUrl(series.Images.Posters.FirstOrDefault());
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
