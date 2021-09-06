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
    public class SeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>
    {
        public string Name => Plugin.MetadataProviderName;

        private readonly IHttpClientFactory HttpClientFactory;

        private readonly ILogger<SeriesProvider> Logger;

        private readonly ShokoAPIManager ApiManager;

        public SeriesProvider(IHttpClientFactory httpClientFactory, ILogger<SeriesProvider> logger, ShokoAPIManager apiManager)
        {
            Logger = logger;
            HttpClientFactory = httpClientFactory;
            ApiManager = apiManager;
        }

        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            try {
                switch (Plugin.Instance.Configuration.SeriesGrouping) {
                    default:
                        return await GetDefaultMetadata(info, cancellationToken);
                    case Ordering.GroupType.ShokoGroup:
                        return await GetShokoGroupedMetadata(info, cancellationToken);
                }
            }
            catch (Exception e) {
                Logger.LogError(e, $"Threw unexpectedly; {e.Message}");
                return new MetadataResult<Series>();
            }
        }

        private async Task<MetadataResult<Series>> GetDefaultMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>();
            var series = await ApiManager.GetSeriesInfoByPath(info.Path);
            if (series == null) {
                Logger.LogWarning("Unable to find group info for path {Path}", info.Path);
                return result;
            }

            var tags = await ApiManager.GetTags(series.Id);
            var ( displayTitle, alternateTitle ) = Text.GetSeriesTitles(series.AniDB.Titles, series.Shoko.Name, info.MetadataLanguage);
            Logger.LogInformation("Found series {SeriesName} (Series={SeriesId})", displayTitle, series.Id);

            result.Item = new Series {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                Overview = Text.GetDescription(series),
                PremiereDate = series.AniDB.AirDate,
                EndDate = series.AniDB.EndDate,
                ProductionYear = series.AniDB.AirDate?.Year,
                Status = series.AniDB.EndDate == null ? SeriesStatus.Continuing : SeriesStatus.Ended,
                Tags = tags,
                CommunityRating = series.AniDB.Rating.ToFloat(10),
            };
            result.Item.SetProviderId("Shoko Series", series.Id);
            if (Plugin.Instance.Configuration.AddAniDBId)
                result.Item.SetProviderId("AniDB", series.AniDB.ID.ToString());
            if (Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.MergeFriendly && !string.IsNullOrEmpty(series.TvDBId))
                result.Item.SetProviderId(MetadataProvider.Tvdb, series.TvDBId);

            result.HasMetadata = true;

            result.ResetPeople();
            foreach (var person in await ApiManager.GetPeople(series.Id))
                result.AddPerson(person);

            return result;
        }

        private async Task<MetadataResult<Series>> GetShokoGroupedMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>();
            var filterLibrary = Plugin.Instance.Configuration.FilterOnLibraryTypes;
            var group = await ApiManager.GetGroupInfoByPath(info.Path, filterLibrary ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default);
            if (group == null) {
                Logger.LogWarning("Unable to find group info for path {Path}", info.Path);
                return result;
            }

            var series = group.DefaultSeries;
            var ( displayTitle, alternateTitle ) = Text.GetSeriesTitles(series.AniDB.Titles, group.Shoko.Name, info.MetadataLanguage);
            Logger.LogInformation("Found series {SeriesName} (Series={SeriesId},Group={GroupId})", displayTitle, series.Id, group.Id);

            var tags = await ApiManager.GetTags(series.Id);
            result.Item = new Series {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                Overview = Text.GetDescription(series),
                PremiereDate = series.AniDB.AirDate,
                EndDate = series.AniDB.EndDate,
                ProductionYear = series.AniDB.AirDate?.Year,
                Status = series.AniDB.EndDate == null ? SeriesStatus.Continuing : SeriesStatus.Ended,
                Tags = tags,
                CommunityRating = series.AniDB.Rating.ToFloat(10),
            };
            // NOTE: This next line will remain here till they fix the series merging for providers outside the MetadataProvider enum.
            result.Item.SetProviderId(MetadataProvider.Imdb, $"INVALID-BUT-DO-NOT-TOUCH:{series.Id}");
            result.Item.SetProviderId("Shoko Series", series.Id);
            result.Item.SetProviderId("Shoko Group", group.Id);
            if (Plugin.Instance.Configuration.AddAniDBId)
                result.Item.SetProviderId("AniDB", series.AniDB.ID.ToString());

            result.HasMetadata = true;

            result.ResetPeople();
            foreach (var person in await ApiManager.GetPeople(series.Id))
                result.AddPerson(person);

            return result;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo info, CancellationToken cancellationToken)
        {
            try {
                var results = new List<RemoteSearchResult>();
                var searchResults = await ShokoAPI.SeriesSearch(info.Name).ContinueWith((e) => e.Result.ToList());
                Logger.LogInformation($"Series search returned {searchResults.Count} results.");

                foreach (var series in searchResults) {
                    var seriesId = series.IDs.ID.ToString();
                    var seriesInfo = ApiManager.GetSeriesInfoSync(seriesId);
                    var imageUrl = seriesInfo.AniDB.Poster?.ToURLString();
                    var parsedSeries = new RemoteSearchResult {
                        Name = Text.GetSeriesTitle(seriesInfo.AniDB.Titles, seriesInfo.Shoko.Name, info.MetadataLanguage),
                        SearchProviderName = Name,
                        ImageUrl = imageUrl,
                    };
                    parsedSeries.SetProviderId("Shoko Series", seriesId);
                    results.Add(parsedSeries);
                }

                return results;
            }
            catch (Exception e) {
                Logger.LogError(e, $"Threw unexpectedly; {e.Message}");
                return new List<RemoteSearchResult>();
            }
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return HttpClientFactory.CreateClient().GetAsync(url, cancellationToken);
        }
    }
}
