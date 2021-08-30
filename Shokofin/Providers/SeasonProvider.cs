using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Utils;

namespace Shokofin.Providers
{
    public class SeasonProvider : IRemoteMetadataProvider<Season, SeasonInfo>
    {
        public string Name => Plugin.MetadataProviderName;
        private readonly IHttpClientFactory HttpClientFactory;
        private readonly ILogger<SeasonProvider> Logger;

        private readonly ShokoAPIManager ApiManager;

        public SeasonProvider(IHttpClientFactory httpClientFactory, ILogger<SeasonProvider> logger, ShokoAPIManager apiManager)
        {
            HttpClientFactory = httpClientFactory;
            Logger = logger;
            ApiManager = apiManager;
        }

        public async Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken cancellationToken)
        {
            try {
                switch (Plugin.Instance.Configuration.SeriesGrouping) {
                    default:
                        return GetDefaultMetadata(info, cancellationToken);
                    case Ordering.GroupType.ShokoGroup:
                        return await GetShokoGroupedMetadata(info, cancellationToken);
                }
            }
            catch (Exception e) {
                Logger.LogError(e, $"Threw unexpectedly; {e.Message}");
                return new MetadataResult<Season>();
            }
        }

        private MetadataResult<Season> GetDefaultMetadata(SeasonInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Season>();

            var seasonName = GetSeasonName(info.Name);
            result.Item = new Season {
                Name = seasonName,
                IndexNumber = info.IndexNumber,
                SortName = seasonName,
                ForcedSortName = seasonName
            };

            result.HasMetadata = true;

            return result;
        }

        private async Task<MetadataResult<Season>> GetShokoGroupedMetadata(SeasonInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Season>();

            if (!info.SeriesProviderIds.ContainsKey("Shoko Group")) {
                Logger.LogWarning($"Shoko Group id not stored for series");
                return result;
            }

            var groupId = info.SeriesProviderIds["Shoko Group"];
            var seasonNumber = info.IndexNumber ?? 1;
            var filterLibrary = Plugin.Instance.Configuration.FilterOnLibraryTypes;
            var series = await ApiManager.GetSeriesInfoFromGroup(groupId, seasonNumber, filterLibrary ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default);
            if (series == null) {
                Logger.LogWarning($"Unable to find series info for G{groupId}:S{seasonNumber}");
                return result;
            }
            Logger.LogInformation($"Found series info for G{groupId}:S{seasonNumber}");

            var tags = await ApiManager.GetTags(series.Id);
            var ( displayTitle, alternateTitle ) = Text.GetSeriesTitles(series.AniDB.Titles, series.Shoko.Name, info.MetadataLanguage);

            result.Item = new Season {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                IndexNumber = seasonNumber,
                Overview = Text.SanitizeTextSummary(series.AniDB.Description),
                PremiereDate = series.AniDB.AirDate,
                EndDate = series.AniDB.EndDate,
                ProductionYear = series.AniDB.AirDate?.Year,
                Tags = tags,
                CommunityRating = series.AniDB.Rating?.ToFloat(10),
            };
            result.Item.ProviderIds.Add("Shoko Series", series.Id);
            if (Plugin.Instance.Configuration.AddAniDBId)
                result.Item.ProviderIds.Add("AniDB", series.AniDB.ID.ToString());

            result.HasMetadata = true;
            ApiManager.MarkSeriesAsFound(series.Id, groupId);

            result.ResetPeople();
            foreach (var person in await ApiManager.GetPeople(series.Id))
                result.AddPerson(person);

            return result;
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo searchInfo, CancellationToken cancellationToken)
        {
            // Isn't called from anywhere. If it is called, I don't know from where.
            throw new NotImplementedException();
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return HttpClientFactory.CreateClient().GetAsync(url, cancellationToken);
        }

        private string GetSeasonName(string season)
        {
            switch (season) {
                case "Season 100":
                    return "Credits";
                case "Season 99":
                    return "Trailers";
                case "Season 98":
                    return "Misc.";
                default:
                    return season;
            }
        }
    }
}
