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
            if (!info.IndexNumber.HasValue) {
                return new MetadataResult<Season>();
            }
            try {
                switch (Plugin.Instance.Configuration.SeriesGrouping) {
                    default:
                        return GetDefaultMetadata(info, cancellationToken);
                    case Ordering.GroupType.ShokoGroup:
                        if (info.IndexNumber.Value == 0)
                            goto default;
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

            var seasonName = GetSeasonName(info.IndexNumber.Value, info.Name);
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

            if (!info.SeriesProviderIds.TryGetValue("Shoko Group", out var groupId)) {
                Logger.LogWarning($"Unable refresh item, Shoko Group Id was not stored for Series.");
                return result;
            }

            var seasonNumber = info.IndexNumber.Value;
            var filterLibrary = Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default;
            var group = await ApiManager.GetGroupInfo(groupId, filterLibrary);
            var series = group?.GetSeriesInfoBySeasonNumber(seasonNumber);
            if (group == null || series == null) {
                Logger.LogWarning("Unable to find info for Season {SeasonNumber} in Series {SeriesName}. (Group={GroupId})", seasonNumber, group.Shoko.Name, groupId);
                return result;
            }
            Logger.LogInformation("Found info for Season {SeasonNumber} in Series {SeriesName} (Group={GroupId},Series={SeriesId})", seasonNumber, group.Shoko.Name, groupId, series.Id);

            var tags = await ApiManager.GetTags(series.Id);
            var ( displayTitle, alternateTitle ) = Text.GetSeriesTitles(series.AniDB.Titles, series.Shoko.Name, info.MetadataLanguage);
            var sortTitle = $"I{seasonNumber} - {series.Shoko.Name}";

            result.Item = new Season {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                IndexNumber = seasonNumber,
                SortName = sortTitle,
                ForcedSortName = sortTitle,
                Overview = Text.GetDescription(series),
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

        private string GetSeasonName(int seasonNumber, string seasonName)
        {
            switch (seasonNumber) {
                case -1:
                    return "Credits";
                case -2:
                    return "Trailers";
                case -3:
                    return "Others";
                case -4:
                    return "Misc.";
                default:
                    return seasonName;
            }
        }
    }
}
