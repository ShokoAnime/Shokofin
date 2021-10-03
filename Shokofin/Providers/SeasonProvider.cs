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
            try {
                switch (Plugin.Instance.Configuration.SeriesGrouping) {
                    default:
                        if (!info.IndexNumber.HasValue)
                            return new MetadataResult<Season>();

                        if (info.IndexNumber.Value == 1)
                            return await GetShokoGroupedMetadata(info, cancellationToken);

                        return GetDefaultMetadata(info, cancellationToken);
                    case Ordering.GroupType.MergeFriendly:
                        return GetDefaultMetadata(info, cancellationToken);
                    case Ordering.GroupType.ShokoGroup:
                        if (info.IndexNumber.HasValue && info.IndexNumber.Value == 0)
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

            int seasonNumber;
            API.Info.SeriesInfo series;
            int offset = 0;
            // All previsouly known seasons
            if (info.ProviderIds.TryGetValue("Shoko Series", out var seriesId) && info.ProviderIds.TryGetValue("Shoko Season Offset", out var offsetText) && int.TryParse(offsetText, out offset)) {
                series = await ApiManager.GetSeriesInfo(seriesId);

                if (series == null) {
                    Logger.LogWarning("Unable to find series info for Season. (Series={SeriesId})", seriesId);
                    return result;
                }

                if (Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup) {
                    var group = await ApiManager.GetGroupInfoForSeries(seriesId);
                    if (group == null) {
                        Logger.LogWarning("Unable to find group info for Season. (Series={SeriesId})", series.Id);
                        return result;
                    }

                    if (!group.SeasonNumberBaseDictionary.TryGetValue(series, out seasonNumber)) {
                        Logger.LogWarning("Unable to find season number for Season. (Series={SeriesId},Group={GroupId})", series.Id, group.Id);
                        return result;
                    }
                    seasonNumber = seasonNumber + (seasonNumber < 0 ? -offset : offset);

                    Logger.LogInformation("Found info for Season {SeasonNumber} in Series {SeriesName} (Series={SeriesId},Group={GroupId})", seasonNumber, group.Shoko.Name, series.Id, group.Id);
                }
                else {
                    seasonNumber = 1 + offset;

                    Logger.LogInformation("Found info for Season {SeasonNumber} in Series {SeriesName} (Series={SeriesId})", seasonNumber, series.Shoko.Name, series.Id);
                }
            }
            // New physical seasons
            else if (info.Path != null) {
                series = await ApiManager.GetSeriesInfoByPath(info.Path);

                if (series == null) {
                    Logger.LogWarning("Unable to find series info for Season by path {Path}.", info.Path);
                    return result;
                }

                if (Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup) {var group = await ApiManager.GetGroupInfoForSeries(seriesId);
                    if (group == null) {
                        Logger.LogWarning("Unable to find group info for Season by path {Path}. (Series={SeriesId})", info.Path, series.Id);
                        return result;
                    }

                    if (!group.SeasonNumberBaseDictionary.TryGetValue(series, out seasonNumber)) {
                        Logger.LogWarning("Unable to find season number for Season by path {Path}. (Series={SeriesId},Group={GroupId})", info.Path, series.Id, group.Id);
                        return result;
                    }

                    Logger.LogInformation("Found info for Season {SeasonNumber} in Series {SeriesName} (Series={SeriesId},Group={GroupId})", seasonNumber, group.Shoko.Name, series.Id, group.Id);
                }
                else {
                    seasonNumber = 1;

                    Logger.LogInformation("Found info for Season {SeasonNumber} in Series {SeriesName} (Series={SeriesId})", seasonNumber, series.Shoko.Name, series.Id);
                }
            }
            // New virtual seasons
            else if (info.SeriesProviderIds.TryGetValue("Shoko Series", out seriesId) && info.IndexNumber.HasValue) {
                seasonNumber = info.IndexNumber.Value;
                if (Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup) {
                    var group = await ApiManager.GetGroupInfoForSeries(seriesId);
                    series = group?.GetSeriesInfoBySeasonNumber(seasonNumber);
                    if (group == null || series == null || !group.SeasonNumberBaseDictionary.TryGetValue(series, out var baseSeasonNumber)) {
                        Logger.LogWarning("Unable to find series info for Season {SeasonNumber}. (Series={SeriesId},Group={GroupId})", seasonNumber, seriesId, group?.Id);
                        return result;
                    }
                    offset = Math.Abs(seasonNumber - baseSeasonNumber);

                    Logger.LogInformation("Found info for Season {SeasonNumber} in Series {SeriesName} (Series={SeriesId},Group={GroupId})", seasonNumber, group.Shoko.Name, series.Id, group.Id);
                }
                else {
                    series = await ApiManager.GetSeriesInfo(seriesId);
                    offset = seasonNumber - 1;

                    if (series == null) {
                        Logger.LogWarning("Unable to find series info for Season {SeasonNumber}. (Series={SeriesId})", seasonNumber, seriesId);
                        return result;
                    }

                    Logger.LogInformation("Found info for Season {SeasonNumber} in Series {SeriesName} (Series={SeriesId})", seasonNumber, series.Shoko.Name, series.Id);
                }
            }
            // Everything else.
            else {
                Logger.LogDebug("Unable refresh Season {SeasonNumber} {SeasonName}", info.IndexNumber, info.Name);
                return result;
            }

            var ( displayTitle, alternateTitle ) = Text.GetSeriesTitles(series.AniDB.Titles, series.Shoko.Name, info.MetadataLanguage);
            var sortTitle = $"I{seasonNumber} - {series.Shoko.Name}";

            if (offset > 0) {
                string type = "";
                switch (offset) {
                    default:
                        break;
                    case -1:
                    case 1:
                        if (series.AlternateEpisodesList.Count > 0)
                            type = "Alternate Stories";
                        else
                            type = "Other Episodes";
                        break;
                    case -2:
                    case 2:
                        type = "Other Episodes";
                        break;
                }
                displayTitle += $" ({type})";
                alternateTitle += $" ({type})";
            }

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
                Studios = series.Studios.ToArray(),
                CommunityRating = series.AniDB.Rating?.ToFloat(10),
            };
            result.Item.ProviderIds.Add("Shoko Series", series.Id);
            result.Item.ProviderIds.Add("Shoko Season Offset", offset.ToString());
            if (Plugin.Instance.Configuration.AddAniDBId)
                result.Item.ProviderIds.Add("AniDB", series.AniDB.ID.ToString());

            result.HasMetadata = true;

            result.ResetPeople();
            foreach (var person in series.Staff)
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
                case 123:
                    return "Unknown";
                default:
                    return seasonName;
            }
        }
    }
}
