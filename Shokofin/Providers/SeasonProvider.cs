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
                            return await GetShokoGroupedMetadata(info);

                        return GetDefaultMetadata(info);
                    case Ordering.GroupType.MergeFriendly:
                        if (!info.IndexNumber.HasValue)
                            return new MetadataResult<Season>();

                        return GetDefaultMetadata(info);
                    case Ordering.GroupType.ShokoGroup:
                        if (info.IndexNumber.HasValue && info.IndexNumber.Value == 0)
                            return GetDefaultMetadata(info);

                        return await GetShokoGroupedMetadata(info);
                }
            }
            catch (Exception ex) {
                Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
                Plugin.Instance.CaptureException(ex);
                return new MetadataResult<Season>();
            }
        }

        private static MetadataResult<Season> GetDefaultMetadata(SeasonInfo info)
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

        private async Task<MetadataResult<Season>> GetShokoGroupedMetadata(SeasonInfo info)
        {
            var result = new MetadataResult<Season>();

            int offset = 0;
            int seasonNumber = 1;
            API.Info.SeasonInfo season;
            // All previously known seasons
            if (info.ProviderIds.TryGetValue("Shoko Series", out var seriesId) && info.ProviderIds.TryGetValue("Shoko Season Offset", out var offsetText) && int.TryParse(offsetText, out offset)) {
                season = await ApiManager.GetSeasonInfoForSeries(seriesId);

                if (season == null) {
                    Logger.LogWarning("Unable to find series info for Season. (Series={SeriesId})", seriesId);
                    return result;
                }

                if (Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup) {
                    var filterByType = Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default;
                    var show = await ApiManager.GetShowInfoForSeries(seriesId, filterByType);
                    if (show == null) {
                        Logger.LogWarning("Unable to find group info for Season. (Series={SeriesId})", season.Id);
                        return result;
                    }

                    if (!show.SeasonNumberBaseDictionary.TryGetValue(season, out seasonNumber)) {
                        Logger.LogWarning("Unable to find season number for Season. (Series={SeriesId},Group={GroupId})", season.Id, show.Id);
                        return result;
                    }
                    seasonNumber = seasonNumber < 0 ? seasonNumber - offset : seasonNumber + offset;

                    Logger.LogInformation("Found info for Season {SeasonNumber} in Series {SeriesName} (Series={SeriesId},Group={GroupId})", seasonNumber, show.Name, season.Id, show.Id);
                }
                else {
                    seasonNumber += offset;

                    Logger.LogInformation("Found info for Season {SeasonNumber} in Series {SeriesName} (Series={SeriesId})", seasonNumber, season.Shoko.Name, season.Id);
                }
            }
            // New physical seasons
            else if (info.Path != null) {
                season = await ApiManager.GetSeasonInfoByPath(info.Path);

                if (season == null) {
                    Logger.LogWarning("Unable to find series info for Season by path {Path}.", info.Path);
                    return result;
                }

                if (Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup) {
                    var filterByType = Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default;
                    var show = await ApiManager.GetShowInfoForSeries(season.Id, filterByType);
                    if (show == null) {
                        Logger.LogWarning("Unable to find group info for Season by path {Path}. (Series={SeriesId})", info.Path, season.Id);
                        return result;
                    }

                    if (!show.SeasonNumberBaseDictionary.TryGetValue(season, out seasonNumber)) {
                        Logger.LogWarning("Unable to find season number for Season by path {Path}. (Series={SeriesId},Group={GroupId})", info.Path, season.Id, show.Id);
                        return result;
                    }

                    Logger.LogInformation("Found info for Season {SeasonNumber} in Series {SeriesName} (Series={SeriesId},Group={GroupId})", seasonNumber, show.Name, season.Id, show.Id);
                }
                else {
                    Logger.LogInformation("Found info for Season {SeasonNumber} in Series {SeriesName} (Series={SeriesId})", seasonNumber, season.Shoko.Name, season.Id);
                }
            }
            // New virtual seasons
            else if (info.SeriesProviderIds.TryGetValue("Shoko Series", out seriesId) && info.IndexNumber.HasValue) {
                seasonNumber = info.IndexNumber.Value;
                if (Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup) {
                    var filterByType = Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default;
                    var show = await ApiManager.GetShowInfoForSeries(seriesId, filterByType);
                    if (show == null) {
                        Logger.LogWarning("Unable to find group info for Season {SeasonNumber}. (Series={SeriesId})", seasonNumber, seriesId);
                        return result;
                    }

                    season = show.GetSeriesInfoBySeasonNumber(seasonNumber);
                    if (season == null || !show.SeasonNumberBaseDictionary.TryGetValue(season, out var baseSeasonNumber)) {
                        Logger.LogWarning("Unable to find series info for Season {SeasonNumber}. (Group={GroupId})", seasonNumber, show.Id);
                        return result;
                    }
                    offset = Math.Abs(seasonNumber - baseSeasonNumber);

                    Logger.LogInformation("Found info for Season {SeasonNumber} in Series {SeriesName} (Series={SeriesId},Group={GroupId})", seasonNumber, show.Name, season.Id, show.Id);
                }
                else {
                    season = await ApiManager.GetSeasonInfoForSeries(seriesId);
                    offset = seasonNumber - 1;

                    if (season == null) {
                        Logger.LogWarning("Unable to find series info for Season {SeasonNumber}. (Series={SeriesId})", seasonNumber, seriesId);
                        return result;
                    }

                    Logger.LogInformation("Found info for Season {SeasonNumber} in Series {SeriesName} (Series={SeriesId})", seasonNumber, season.Shoko.Name, season.Id);
                }
            }
            // Everything else.
            else {
                Logger.LogDebug("Unable refresh Season {SeasonNumber} {SeasonName}", info.IndexNumber, info.Name);
                return result;
            }

            result.Item = CreateMetadata(season, seasonNumber, offset, info.MetadataLanguage);

            result.HasMetadata = true;

            result.ResetPeople();
            foreach (var person in season.Staff)
                result.AddPerson(person);

            return result;
        }

        public static Season CreateMetadata(Info.SeasonInfo seasonInfo, int seasonNumber, int offset, string metadataLanguage)
            => CreateMetadata(seasonInfo, seasonNumber, offset, metadataLanguage, null, Guid.Empty);

        public static Season CreateMetadata(Info.SeasonInfo seasonInfo, int seasonNumber, int offset, Series series, System.Guid seasonId)
            => CreateMetadata(seasonInfo, seasonNumber, offset, series.GetPreferredMetadataLanguage(), series, seasonId);

        public static Season CreateMetadata(Info.SeasonInfo seasonInfo, int seasonNumber, int offset, string metadataLanguage, Series series, System.Guid seasonId)
        {
            var ( displayTitle, alternateTitle ) = Text.GetSeriesTitles(seasonInfo.AniDB.Titles, seasonInfo.Shoko.Name, metadataLanguage);
            var sortTitle = $"S{seasonNumber} - {seasonInfo.Shoko.Name}";

            if (offset > 0) {
                string type = "";
                switch (offset) {
                    default:
                        break;
                    case -1:
                    case 1:
                        if (seasonInfo.AlternateEpisodesList.Count > 0)
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
                    Overview = Text.GetDescription(seasonInfo),
                    PremiereDate = seasonInfo.AniDB.AirDate,
                    EndDate = seasonInfo.AniDB.EndDate,
                    ProductionYear = seasonInfo.AniDB.AirDate?.Year,
                    Tags = seasonInfo.Tags.ToArray(),
                    Genres = seasonInfo.Genres.ToArray(),
                    Studios = seasonInfo.Studios.ToArray(),
                    CommunityRating = seasonInfo.AniDB.Rating?.ToFloat(10),
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
                    Overview = Text.GetDescription(seasonInfo),
                    PremiereDate = seasonInfo.AniDB.AirDate,
                    EndDate = seasonInfo.AniDB.EndDate,
                    ProductionYear = seasonInfo.AniDB.AirDate?.Year,
                    Tags = seasonInfo.Tags.ToArray(),
                    Genres = seasonInfo.Genres.ToArray(),
                    Studios = seasonInfo.Studios.ToArray(),
                    CommunityRating = seasonInfo.AniDB.Rating?.ToFloat(10),
                };
            }
            season.ProviderIds.Add("Shoko Series", seasonInfo.Id);
            season.ProviderIds.Add("Shoko Season Offset", offset.ToString());
            if (Plugin.Instance.Configuration.AddAniDBId)
                season.ProviderIds.Add("AniDB", seasonInfo.AniDB.Id.ToString());

            return season;
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo searchInfo, CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<RemoteSearchResult>>(new List<RemoteSearchResult>());

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
            => HttpClientFactory.CreateClient().GetAsync(url, cancellationToken);

        private static string GetSeasonName(int seasonNumber, string seasonName)
            => seasonNumber switch
            {
                127 => "Misc.",
                126 => "Credits",
                125 => "Trailers",
                124 => "Others",
                123 => "Unknown",
                _ => seasonName,
            };
    }
}
