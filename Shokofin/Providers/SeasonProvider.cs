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

using Info = Shokofin.API.Info;

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
                        if (!info.IndexNumber.HasValue)
                            return new MetadataResult<Season>();

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

            int offset = 0;
            int seasonNumber = 1;
            API.Info.SeriesInfo series;
            // All previously known seasons
            if (info.ProviderIds.TryGetValue("Shoko Series", out var seriesId) && info.ProviderIds.TryGetValue("Shoko Season Offset", out var offsetText) && int.TryParse(offsetText, out offset)) {
                series = await ApiManager.GetSeriesInfo(seriesId);

                if (series == null) {
                    Logger.LogWarning("Unable to find series info for Season. (Series={SeriesId})", seriesId);
                    return result;
                }

                if (Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup) {
                    var filterByType = Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default;
                    var group = await ApiManager.GetGroupInfoForSeries(seriesId, filterByType);
                    if (group == null) {
                        Logger.LogWarning("Unable to find group info for Season. (Series={SeriesId})", series.Id);
                        return result;
                    }

                    if (!group.SeasonNumberBaseDictionary.TryGetValue(series, out seasonNumber)) {
                        Logger.LogWarning("Unable to find season number for Season. (Series={SeriesId},Group={GroupId})", series.Id, group.Id);
                        return result;
                    }
                    seasonNumber = seasonNumber < 0 ? seasonNumber - offset : seasonNumber + offset;

                    Logger.LogInformation("Found info for Season {SeasonNumber} in Series {SeriesName} (Series={SeriesId},Group={GroupId})", seasonNumber, group.Shoko.Name, series.Id, group.Id);
                }
                else {
                    seasonNumber += offset;

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

                if (Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup) {
                    var filterByType = Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default;
                    var group = await ApiManager.GetGroupInfoForSeries(series.Id, filterByType);
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
                    Logger.LogInformation("Found info for Season {SeasonNumber} in Series {SeriesName} (Series={SeriesId})", seasonNumber, series.Shoko.Name, series.Id);
                }
            }
            // New virtual seasons
            else if (info.SeriesProviderIds.TryGetValue("Shoko Series", out seriesId) && info.IndexNumber.HasValue) {
                seasonNumber = info.IndexNumber.Value;
                if (Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup) {
                    var filterByType = Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default;
                    var group = await ApiManager.GetGroupInfoForSeries(seriesId, filterByType);
                    if (group == null) {
                        Logger.LogWarning("Unable to find group info for Season {SeasonNumber}. (Series={SeriesId})", seasonNumber, seriesId);
                        return result;
                    }

                    series = group.GetSeriesInfoBySeasonNumber(seasonNumber);
                    if (series == null || !group.SeasonNumberBaseDictionary.TryGetValue(series, out var baseSeasonNumber)) {
                        Logger.LogWarning("Unable to find series info for Season {SeasonNumber}. (Group={GroupId})", seasonNumber, group.Id);
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

            result.Item = CreateMetadata(series, seasonNumber, offset, info.MetadataLanguage);

            result.HasMetadata = true;

            result.ResetPeople();
            foreach (var person in series.Staff)
                result.AddPerson(person);

            return result;
        }

        public static Season CreateMetadata(Info.SeriesInfo seriesInfo, int seasonNumber, int offset, string metadataLanguage)
            => CreateMetadata(seriesInfo, seasonNumber, offset, metadataLanguage, null, Guid.Empty);

        public static Season CreateMetadata(Info.SeriesInfo seriesInfo, int seasonNumber, int offset, Series series, System.Guid seasonId)
            => CreateMetadata(seriesInfo, seasonNumber, offset, series.GetPreferredMetadataLanguage(), series, seasonId);

        public static Season CreateMetadata(Info.SeriesInfo seriesInfo, int seasonNumber, int offset, string metadataLanguage, Series series, System.Guid seasonId)
        {
            var ( displayTitle, alternateTitle ) = Text.GetSeriesTitles(seriesInfo.AniDB.Titles, seriesInfo.Shoko.Name, metadataLanguage);
            var sortTitle = $"S{seasonNumber} - {seriesInfo.Shoko.Name}";

            if (offset > 0) {
                string type = "";
                switch (offset) {
                    default:
                        break;
                    case -1:
                    case 1:
                        if (seriesInfo.AlternateEpisodesList.Count > 0)
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

            Season season;
            if (series != null) {
                season = new Season {
                    Name = displayTitle,
                    OriginalTitle = alternateTitle,
                    IndexNumber = seasonNumber,
                    SortName = sortTitle,
                    ForcedSortName = sortTitle,
                    Id = seasonId,
                    IsVirtualItem = true,
                    Overview = Text.GetDescription(seriesInfo),
                    PremiereDate = seriesInfo.AniDB.AirDate,
                    EndDate = seriesInfo.AniDB.EndDate,
                    ProductionYear = seriesInfo.AniDB.AirDate?.Year,
                    Tags = seriesInfo.Tags.ToArray(),
                    Genres = seriesInfo.Genres.ToArray(),
                    Studios = seriesInfo.Studios.ToArray(),
                    CommunityRating = seriesInfo.AniDB.Rating?.ToFloat(10),
                    SeriesId = series.Id,
                    SeriesName = series.Name,
                    SeriesPresentationUniqueKey = series.GetPresentationUniqueKey(),
                    DateModified = DateTime.UtcNow,
                    DateLastSaved = DateTime.UtcNow,
                };
            }
            else {
                season = new Season {
                    Name = displayTitle,
                    OriginalTitle = alternateTitle,
                    IndexNumber = seasonNumber,
                    SortName = sortTitle,
                    ForcedSortName = sortTitle,
                    Overview = Text.GetDescription(seriesInfo),
                    PremiereDate = seriesInfo.AniDB.AirDate,
                    EndDate = seriesInfo.AniDB.EndDate,
                    ProductionYear = seriesInfo.AniDB.AirDate?.Year,
                    Tags = seriesInfo.Tags.ToArray(),
                    Genres = seriesInfo.Genres.ToArray(),
                    Studios = seriesInfo.Studios.ToArray(),
                    CommunityRating = seriesInfo.AniDB.Rating?.ToFloat(10),
                };
            }
            season.ProviderIds.Add("Shoko Series", seriesInfo.Id);
            season.ProviderIds.Add("Shoko Season Offset", offset.ToString());
            if (Plugin.Instance.Configuration.AddAniDBId)
                season.ProviderIds.Add("AniDB", seriesInfo.AniDB.Id.ToString());

            return season;
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
