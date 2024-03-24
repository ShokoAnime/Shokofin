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

#nullable enable
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
                return Plugin.Instance.Configuration.BoxSetGrouping switch
                {
                    Ordering.GroupType.ShokoGroup => await GetShokoGroupedMetadata(info),
                    Ordering.GroupType.ShokoGroupPlus => await GetShokoGroupedMetadata(info),
                    _ => await GetDefaultMetadata(info),
                };
            }
            catch (Exception ex) {
                Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
                Plugin.Instance.CaptureException(ex);
                return new MetadataResult<BoxSet>();
            }
        }

        public async Task<MetadataResult<BoxSet>> GetDefaultMetadata(BoxSetInfo info)
        {
            var result = new MetadataResult<BoxSet>();

            // First try to re-use any existing series id.
            API.Info.SeasonInfo? season = null;
            if (info.ProviderIds.TryGetValue("Shoko Series", out var seriesId))
                season = await ApiManager.GetSeasonInfoForSeries(seriesId);

            // Then try to look ir up by path.
            if (season == null)
                season = await ApiManager.GetSeasonInfoByPath(info.Path);

            // Then try to look it up using the name.
            if (season == null && TryGetBoxSetName(info, out var boxSetName))
                season = await ApiManager.GetSeasonInfoBySeriesName(boxSetName);

            if (season == null) {
                    Logger.LogWarning("Unable to find movie box-set info for name {Name} and path {Path}", info.Name, info.Path);
                return result;
            }

            if (season.EpisodeList.Count <= 1) {
                Logger.LogWarning("Series did not contain multiple movies! Skipping path {Path} (Series={SeriesId})", info.Path, season.Id);
                return result;
            }

            var ( displayTitle, alternateTitle ) = Text.GetSeriesTitles(season.AniDB.Titles, season.AniDB.Title, info.MetadataLanguage);

            result.Item = new BoxSet {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                Overview = Text.GetDescription(season),
                PremiereDate = season.AniDB.AirDate,
                EndDate = season.AniDB.EndDate,
                ProductionYear = season.AniDB.AirDate?.Year,
                Tags = season.Tags.ToArray(),
                CommunityRating = season.AniDB.Rating.ToFloat(10),
            };
            result.Item.SetProviderId("Shoko Series", season.Id);
            if (Plugin.Instance.Configuration.AddAniDBId)
                result.Item.SetProviderId("AniDB", season.AniDB.Id.ToString());

            result.HasMetadata = true;

            return result;
        }

        private async Task<MetadataResult<BoxSet>> GetShokoGroupedMetadata(BoxSetInfo info)
        {
            var result = new MetadataResult<BoxSet>();
            var config = Plugin.Instance.Configuration;
            Ordering.GroupFilterType filterByType = config.FilterOnLibraryTypes ? Ordering.GroupFilterType.Movies : Ordering.GroupFilterType.Default;

            // First try to re-use any existing group id.
            API.Info.CollectionInfo? collection = null;
            if (info.ProviderIds.TryGetValue("Shoko Group", out var groupId))
                collection = await ApiManager.GetCollectionInfoForGroup(groupId, filterByType);

            // Then try to look it up by path.
            if (collection == null)
                collection = await ApiManager.GetCollectionInfoByPath(info.Path, filterByType);

            // Then try to look it up using the name.
            if (collection == null && TryGetBoxSetName(info, out var boxSetName))
                collection = await ApiManager.GetCollectionInfoBySeriesName(boxSetName, filterByType);

            if (collection == null) {
                Logger.LogWarning("Unable to find collection info for name {Name} and path {Path}", info.Name, info.Path);
                return result;
            }

            result.Item = new BoxSet {
                Name = collection.Name,
                Overview = collection.Shoko.Description,
            };
            result.Item.SetProviderId("Shoko Group", collection.Id);
            result.HasMetadata = true;

            return result;
        }

        private static bool TryGetBoxSetName(BoxSetInfo info, out string boxSetName)
        {
            if (string.IsNullOrWhiteSpace(info.Name)) {
                boxSetName = string.Empty;
                return false;
            }

            var name = info.Name.Trim();
            if (name.EndsWith("[boxset]"))
                name = name[..^8].TrimEnd();
            if (string.IsNullOrWhiteSpace(name)) {
                boxSetName = string.Empty;
                return false;
            }

            boxSetName = name;
            return true;
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BoxSetInfo searchInfo, CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<RemoteSearchResult>>(new List<RemoteSearchResult>());


        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
            => HttpClientFactory.CreateClient().GetAsync(url, cancellationToken);
    }
}
