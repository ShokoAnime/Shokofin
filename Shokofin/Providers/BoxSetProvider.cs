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
    public class BoxSetProvider : IRemoteMetadataProvider<BoxSet, BoxSetInfo>
    {
        public string Name => Plugin.MetadataProviderName;

        private readonly IHttpClientFactory HttpClientFactory;

        private readonly ILogger<BoxSetProvider> Logger;

        private readonly ShokoAPIManager ApiManager;

        public BoxSetProvider(IHttpClientFactory httpClientFactory, ILogger<BoxSetProvider> logger, ShokoAPIManager apiManager)
        {
            HttpClientFactory = httpClientFactory;
            Logger = logger;
            ApiManager = apiManager;
        }

        public async Task<MetadataResult<BoxSet>> GetMetadata(BoxSetInfo info, CancellationToken cancellationToken)
        {
            try {
                switch (Plugin.Instance.Configuration.BoxSetGrouping) {
                    default:
                        return await GetDefaultMetadata(info, cancellationToken);
                    case Ordering.GroupType.ShokoGroup:
                        return await GetShokoGroupedMetadata(info, cancellationToken);
                }
            }
            catch (Exception e) {
                Logger.LogError(e, $"Threw unexpectedly; {e.Message}");
                return new MetadataResult<BoxSet>();
            }
        }

        public async Task<MetadataResult<BoxSet>> GetDefaultMetadata(BoxSetInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BoxSet>();
            var series = await ApiManager.GetSeriesInfoByPath(info.Path);

            if (series == null) {
                Logger.LogWarning($"Unable to find series info for path {info.Path}");
                return result;
            }

            int aniDBId = series.AniDB.ID;

            if (series.Shoko.Sizes.Total.Episodes <= 1) {
                Logger.LogWarning($"series did not contain multiple movies! Skipping path {info.Path}");
                return result;
            }

            var ( displayTitle, alternateTitle ) = Text.GetSeriesTitles(series.AniDB.Titles, series.AniDB.Title, info.MetadataLanguage);
            var tags = await ApiManager.GetTags(series.Id);

            result.Item = new BoxSet {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                Overview = Text.GetDescription(series),
                PremiereDate = series.AniDB.AirDate,
                EndDate = series.AniDB.EndDate,
                ProductionYear = series.AniDB.AirDate?.Year,
                Tags = tags,
                CommunityRating = series.AniDB.Rating.ToFloat(10),
            };
            result.Item.SetProviderId("Shoko Series", series.Id);
            if (Plugin.Instance.Configuration.AddAniDBId)
                result.Item.SetProviderId("AniDB", series.AniDB.ID.ToString());

            result.HasMetadata = true;

            return result;
        }

        private async Task<MetadataResult<BoxSet>> GetShokoGroupedMetadata(BoxSetInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BoxSet>();
            var config = Plugin.Instance.Configuration;
            Ordering.GroupFilterType filterByType = config.FilterOnLibraryTypes ? Ordering.GroupFilterType.Movies : Ordering.GroupFilterType.Default;
            var group = await ApiManager.GetGroupInfoByPath(info.Path, filterByType);
            if (group == null) {
                Logger.LogWarning($"Unable to find box-set info for path {info.Path}");
                return result;
            }

            var series = group.DefaultSeries;
            if (series.AniDB.Type != API.Models.SeriesType.Movie) {
                Logger.LogWarning($"File found, but not a movie! Skipping.");
                return result;
            }

            var tags = await ApiManager.GetTags(series.Id);
            var ( displayTitle, alternateTitle ) = Text.GetSeriesTitles(series.AniDB.Titles, series.Shoko.Name, info.MetadataLanguage);

            result.Item = new BoxSet {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                Overview = Text.GetDescription(series),
                PremiereDate = series.AniDB.AirDate,
                EndDate = series.AniDB.EndDate,
                ProductionYear = series.AniDB.AirDate?.Year,
                Tags = tags,
                CommunityRating = (float)((series.AniDB.Rating.Value * 10) / series.AniDB.Rating.MaxValue)
            };
            result.Item.SetProviderId("Shoko Series", series.Id);
            result.Item.SetProviderId("Shoko Group", group.Id);
            if (config.AddAniDBId)
                result.Item.SetProviderId("AniDB", series.AniDB.ID.ToString());

            result.HasMetadata = true;

            result.ResetPeople();
            foreach (var person in await ApiManager.GetPeople(series.Id))
                result.AddPerson(person);

            return result;
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BoxSetInfo searchInfo, CancellationToken cancellationToken)
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
