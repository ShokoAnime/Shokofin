using System;
using System.Collections.Generic;
using System.Linq;
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
                        if (info.IndexNumber.Value == 1)
                            return await GetShokoGroupedMetadata(info, cancellationToken);
                        return GetDefaultMetadata(info, cancellationToken);
                    case Ordering.GroupType.MergeFriendly:
                        return GetDefaultMetadata(info, cancellationToken);
                    case Ordering.GroupType.ShokoGroup:
                        if (info.IndexNumber.Value == 0)
                            return GetDefaultMetadata(info, cancellationToken);
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

            if (!info.SeriesProviderIds.TryGetValue("Shoko Series", out var seriesId)) {
                Logger.LogWarning($"Unable refresh item, Shoko Group Id was not stored for Series.");
                return result;
            }

            var seasonNumber = info.IndexNumber.Value;
            API.Info.SeriesInfo series;
            if (Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup) {
                var filterLibrary = Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default;
                var group = await ApiManager.GetGroupInfoForSeries(seriesId, filterLibrary);
                series = group?.GetSeriesInfoBySeasonNumber(seasonNumber);
                if (group == null || series == null) {
                    Logger.LogWarning("Unable to find info for Season {SeasonNumber}. (Series={SeriesId})", seasonNumber, series.Id);
                    return result;
                }
                Logger.LogInformation("Found info for Season {SeasonNumber} in Series {SeriesName} (Group={GroupId},Series={SeriesId})", seasonNumber, group.Shoko.Name, group.Id, series.Id);
            }
            else {
                series = await ApiManager.GetSeriesInfo(seriesId);
                if (series == null) {
                    Logger.LogWarning("Unable to find info for Season {SeasonNumber}. (Series={SeriesId})", seasonNumber, series.Id);
                    return result;
                }
                Logger.LogInformation("Found info for Season {SeasonNumber} in Series {SeriesName} (Series={SeriesId})", seasonNumber, series.Shoko.Name, series.Id);
            }

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
                Tags = series.Tags.ToArray(),
                Genres = series.Genres.ToArray(),
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
                case 127:
                    return "Misc.";
                case 126:
                    return "Credits";
                case 125:
                    return "Trailers";
                case 124:
                    return "Others";
                default:
                    return seasonName;
            }
        }
    }
}
