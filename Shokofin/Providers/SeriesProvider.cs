using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
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

        private readonly ShokoAPIClient ApiClient;

        private readonly ShokoAPIManager ApiManager;

        private readonly IFileSystem FileSystem;

        public SeriesProvider(IHttpClientFactory httpClientFactory, ILogger<SeriesProvider> logger, ShokoAPIClient apiClient, ShokoAPIManager apiManager, IFileSystem fileSystem)
        {
            Logger = logger;
            HttpClientFactory = httpClientFactory;
            ApiClient = apiClient;
            ApiManager = apiManager;
            FileSystem = fileSystem;
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
            catch (Exception ex) {
                Logger.LogError(ex, $"Threw unexpectedly; {ex.Message}");
                Plugin.Instance.CaptureException(ex);
                return new MetadataResult<Series>();
            }
        }

        private async Task<MetadataResult<Series>> GetDefaultMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>();
            var series = await ApiManager.GetSeriesInfoByPath(info.Path);
            if (series == null) {
                // Look for the "season" directories to probe for the series information
                var entries = FileSystem.GetDirectories(info.Path, false);
                foreach (var entry in entries) {
                    series = await ApiManager.GetSeriesInfoByPath(entry.FullName);
                    if (series != null)
                        break;
                }
                if (series == null) {
                    Logger.LogWarning("Unable to find series info for path {Path}", info.Path);
                    return result;
                }
            }

            var mergeFriendly = Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.MergeFriendly && series.TvDB != null;

            var defaultSeriesTitle = mergeFriendly ? series.TvDB.Title : series.Shoko.Name;
            var ( displayTitle, alternateTitle ) = Text.GetSeriesTitles(series.AniDB.Titles, defaultSeriesTitle, info.MetadataLanguage);
            Logger.LogInformation("Found series {SeriesName} (Series={SeriesId})", displayTitle, series.Id);

            if (mergeFriendly) {
                result.Item = new Series {
                    Name = displayTitle,
                    OriginalTitle = alternateTitle,
                    Overview = Text.GetDescription(series),
                    PremiereDate = series.TvDB.AirDate,
                    EndDate = series.TvDB.EndDate,
                    ProductionYear = series.TvDB.AirDate?.Year,
                    Status = series.TvDB.EndDate == null ? SeriesStatus.Continuing : SeriesStatus.Ended,
                    Tags = series.Tags.ToArray(),
                    Genres = series.Genres.ToArray(),
                    Studios = series.Studios.ToArray(),
                    CommunityRating = series.TvDB.Rating?.ToFloat(10),
                };
            }
            else {
                result.Item = new Series {
                    Name = displayTitle,
                    OriginalTitle = alternateTitle,
                    Overview = Text.GetDescription(series),
                    PremiereDate = series.AniDB.AirDate,
                    EndDate = series.AniDB.EndDate,
                    ProductionYear = series.AniDB.AirDate?.Year,
                    Status = series.AniDB.EndDate == null ? SeriesStatus.Continuing : SeriesStatus.Ended,
                    Tags = series.Tags.ToArray(),
                    Genres = series.Genres.ToArray(),
                    Studios = series.Studios.ToArray(),
                    OfficialRating = series.AniDB.Restricted ? "XXX" : null,
                    CustomRating = series.AniDB.Restricted ? "XXX" : null,
                    CommunityRating = series.AniDB.Rating.ToFloat(10),
                };
            }
            AddProviderIds(result.Item, seriesId: series.Id, anidbId: series.AniDB.Id.ToString(), tvdbId: mergeFriendly ? series.TvDB.Id.ToString() : null, tmdbId: series.Shoko.IDs.TMDB.FirstOrDefault().ToString());

            result.HasMetadata = true;

            result.ResetPeople();
            foreach (var person in series.Staff)
                result.AddPerson(person);

            return result;
        }

        private async Task<MetadataResult<Series>> GetShokoGroupedMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>();
            var filterLibrary = Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default;
            var group = await ApiManager.GetGroupInfoByPath(info.Path, filterLibrary);
            if (group == null) {
                // Look for the "season" directories to probe for the group information
                var entries = FileSystem.GetDirectories(info.Path, false);
                foreach (var entry in entries) {
                    group = await ApiManager.GetGroupInfoByPath(entry.FullName, filterLibrary);
                    if (group != null)
                        break;
                }
                if (group == null) {
                    Logger.LogWarning("Unable to find group info for path {Path}", info.Path);
                    return result;
                }
            }

            var series = group.DefaultSeries;
            var premiereDate = group.SeriesList
                .Select(s => s.AniDB.AirDate)
                .Where(s => s != null)
                .OrderBy(s => s)
                .FirstOrDefault();
            var endDate = group.SeriesList.Any(s => s.AniDB.EndDate == null) ? null : group.SeriesList
                .Select(s => s.AniDB.AirDate)
                .OrderBy(s => s)
                .LastOrDefault();
            var ( displayTitle, alternateTitle ) = Text.GetSeriesTitles(series.AniDB.Titles, group.Shoko.Name, info.MetadataLanguage);
            Logger.LogInformation("Found series {SeriesName} (Series={SeriesId},Group={GroupId})", displayTitle, series.Id, group.Id);

            result.Item = new Series {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                Overview = Text.GetDescription(series),
                PremiereDate = premiereDate,
                ProductionYear = premiereDate?.Year,
                EndDate = endDate,
                Status = endDate == null ? SeriesStatus.Continuing : SeriesStatus.Ended,
                Tags = group.Tags.ToArray(),
                Genres = group.Genres.ToArray(),
                Studios = group.Studios.ToArray(),
                OfficialRating = series.AniDB.Restricted ? "XXX" : null,
                CustomRating = series.AniDB.Restricted ? "XXX" : null,
                CommunityRating = series.AniDB.Rating.ToFloat(10),
            };
            AddProviderIds(result.Item, seriesId: series.Id, groupId: group.Id, anidbId: series.AniDB.Id.ToString());

            result.HasMetadata = true;

            result.ResetPeople();
            foreach (var person in series.Staff)
                result.AddPerson(person);

            return result;
        }

        public static void AddProviderIds(IHasProviderIds item, string seriesId, string groupId = null, string anidbId = null, string tvdbId = null, string tmdbId = null)
        {
            // NOTE: These next two lines will remain here till _someone_ fix the series merging for providers other then TvDB and ImDB in Jellyfin.
            // NOTE: #2 Will fix this once JF 10.9 is out, as it contains a change that will help in this situation.
            if (string.IsNullOrEmpty(tvdbId))
                item.SetProviderId(MetadataProvider.Imdb, $"INVALID-BUT-DO-NOT-TOUCH:{seriesId}");

            item.SetProviderId("Shoko Series", seriesId);
            if (!string.IsNullOrEmpty(groupId))
                item.SetProviderId("Shoko Group", groupId);
            if (Plugin.Instance.Configuration.AddAniDBId && !string.IsNullOrEmpty(anidbId) && anidbId != "0")
                item.SetProviderId("AniDB", anidbId);
            if (Plugin.Instance.Configuration.AddTvDBId &&!string.IsNullOrEmpty(tvdbId) && tvdbId != "0")
                item.SetProviderId(MetadataProvider.Tvdb, tvdbId);
            if (Plugin.Instance.Configuration.AddTMDBId &&!string.IsNullOrEmpty(tmdbId) && tmdbId != "0")
                item.SetProviderId(MetadataProvider.Tvdb, tmdbId);
        }
        

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo info, CancellationToken cancellationToken)
        {
            try {
                var searchResults = await ApiClient.SeriesSearch(info.Name).ContinueWith((e) => e.Result.List);
                Logger.LogInformation($"Series search returned {searchResults.Count} results.");
                return searchResults.Select(series => {
                    var seriesId = (series?.ShokoId ?? 0).ToString();
                    var imageUrl = series?.Poster.IsAvailable ?? false ? series.Poster.ToPrettyURLString() : null;
                    var parsedSeries = new RemoteSearchResult {
                        Name = Text.GetSeriesTitle(series.Titles, series.Title, info.MetadataLanguage),
                        SearchProviderName = Name,
                        ImageUrl = imageUrl,
                        Overview = Text.SanitizeTextSummary(series.Description),
                    };
                    AddProviderIds(parsedSeries, seriesId: seriesId, groupId: null, anidbId: series.Id.ToString(), tvdbId: null);
                    return parsedSeries;
                });
            }
            catch (Exception ex) {
                Logger.LogError(ex, $"Threw unexpectedly; {ex.Message}");
                Plugin.Instance.CaptureException(ex);
                return new List<RemoteSearchResult>();
            }
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return HttpClientFactory.CreateClient().GetAsync(url, cancellationToken);
        }
    }
}
