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

namespace Shokofin.Providers
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
                switch (Plugin.Instance.Configuration.SeriesGrouping)
                {
                    default:
                        return await GetDefaultMetadata(info, cancellationToken);
                    case OrderingUtil.SeriesGroupType.ShokoGroup:
                        return await GetShokoGroupedMetadata(info, cancellationToken);
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"{e.Message}{Environment.NewLine}{e.StackTrace}");
                return new MetadataResult<Series>();
            }
        }

        private async Task<MetadataResult<Series>> GetDefaultMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>();
            var (id, series) = await DataUtil.GetSeriesInfoByPath(info.Path);
            if (series == null)
            {
                _logger.LogWarning($"Unable to find series info for path {id}");
                return result;
            }
            _logger.LogInformation($"Found series info for path {id}");

            var tags = await DataUtil.GetTags(series.ID);
            var ( displayTitle, alternateTitle ) = TextUtil.GetSeriesTitles(series.AniDB.Titles, series.Shoko.Name, info.MetadataLanguage);

            result.Item = new Series
            {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                Overview = TextUtil.SummarySanitizer(series.AniDB.Description),
                PremiereDate = series.AniDB.AirDate,
                EndDate = series.AniDB.EndDate,
                ProductionYear = series.AniDB.AirDate?.Year,
                Status = series.AniDB.EndDate == null ? SeriesStatus.Continuing : SeriesStatus.Ended,
                Tags = tags,
                CommunityRating = (float)((series.AniDB.Rating.Value * 10) / series.AniDB.Rating.MaxValue)
            };

            result.Item.SetProviderId("Shoko Series", series.ID);
            result.Item.SetProviderId("AniDB", series.AniDB.ID.ToString());
            if (!string.IsNullOrEmpty(series.TvDBID)) result.Item.SetProviderId("Tvdb", series.TvDBID);
            result.HasMetadata = true;

            result.ResetPeople();
            foreach (var person in await DataUtil.GetPeople(series.ID))
                result.AddPerson(person);

            return result;
        }

        private async Task<MetadataResult<Series>> GetShokoGroupedMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>();
            var (id, group) = await DataUtil.GetGroupInfoByPath(info.Path);
            if (group == null)
            {
                _logger.LogWarning($"Unable to find series info for path {id}");
                return result;
            }
            _logger.LogInformation($"Found series info for path {id}");

            var series = group.DefaultSeries;
            var tvdbId = series?.TvDBID;

            var tags = await DataUtil.GetTags(series.ID);
            var ( displayTitle, alternateTitle ) = TextUtil.GetSeriesTitles(series.AniDB.Titles, series.Shoko.Name, info.MetadataLanguage);

            result.Item = new Series
            {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                Overview = TextUtil.SummarySanitizer(series.AniDB.Description),
                PremiereDate = series.AniDB.AirDate,
                EndDate = series.AniDB.EndDate,
                ProductionYear = series.AniDB.AirDate?.Year,
                Status = series.AniDB.EndDate == null ? SeriesStatus.Continuing : SeriesStatus.Ended,
                Tags = tags,
                CommunityRating = (float)((series.AniDB.Rating.Value * 10) / series.AniDB.Rating.MaxValue)
            };

            result.Item.SetProviderId("Shoko Series", series.ID);
            result.Item.SetProviderId("Shoko Group", group.ID);
            result.Item.SetProviderId("AniDB", series.AniDB.ID.ToString());
            if (!string.IsNullOrEmpty(tvdbId)) result.Item.SetProviderId("Tvdb", tvdbId);
            result.HasMetadata = true;

            result.ResetPeople();
            foreach (var person in await DataUtil.GetPeople(series.ID))
                result.AddPerson(person);

            return result;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Searching Series ({searchInfo.Name})");
            var searchResults = await ShokoAPI.SeriesSearch(searchInfo.Name);

            if (searchResults.Count() == 0) searchResults = await ShokoAPI.SeriesStartsWith(searchInfo.Name);

            var results = new List<RemoteSearchResult>();

            foreach (var series in searchResults)
            {
                var imageUrl = DataUtil.GetImageUrl(series.Images.Posters.FirstOrDefault());
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
    }
}
