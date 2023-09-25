using System;
using System.Collections.Generic;
using System.IO;
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

        private readonly ShokoAPIManager ApiManager;

        private readonly IFileSystem FileSystem;

        public SeriesProvider(IHttpClientFactory httpClientFactory, ILogger<SeriesProvider> logger, ShokoAPIManager apiManager, IFileSystem fileSystem)
        {
            Logger = logger;
            HttpClientFactory = httpClientFactory;
            ApiManager = apiManager;
            FileSystem = fileSystem;
        }

        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            try {
                var result = new MetadataResult<Series>();
                var filterLibrary = Plugin.Instance.Configuration.FilterOnLibraryTypes ? Ordering.GroupFilterType.Others : Ordering.GroupFilterType.Default;
                var show = await ApiManager.GetShowInfoByPath(info.Path, filterLibrary);
                if (show == null) {
                    try {
                        // Look for the "season" directories to probe for the group information
                        var entries = FileSystem.GetDirectories(info.Path, false);
                        foreach (var entry in entries) {
                            show = await ApiManager.GetShowInfoByPath(entry.FullName, filterLibrary);
                            if (show != null)
                                break;
                        }
                        if (show == null) {
                            Logger.LogWarning("Unable to find show info for path {Path}", info.Path);
                            return result;
                        }
                    }
                    catch (DirectoryNotFoundException) {
                        return result;
                    }
                }

                var season = show.DefaultSeason;
                var mergeFriendly = Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.MergeFriendly && season.TvDB != null;
                var defaultSeriesTitle = mergeFriendly ? season.TvDB.Title : season.Shoko.Name;
                var ( displayTitle, alternateTitle ) = Text.GetSeriesTitles(season.AniDB.Titles, show.Name, info.MetadataLanguage);
                Logger.LogInformation("Found series {SeriesName} (Series={SeriesId},Group={GroupId})", displayTitle, season.Id, show.Id);
                if (mergeFriendly) {
                    result.Item = new Series {
                        Name = displayTitle,
                        OriginalTitle = alternateTitle,
                        Overview = Text.GetDescription(season),
                        PremiereDate = season.TvDB.AirDate,
                        EndDate = season.TvDB.EndDate,
                        ProductionYear = season.TvDB.AirDate?.Year,
                        Status = !season.TvDB.EndDate.HasValue || season.TvDB.EndDate.Value > DateTime.UtcNow ? SeriesStatus.Continuing : SeriesStatus.Ended,
                        Tags = season.Tags.ToArray(),
                        Genres = season.Genres.ToArray(),
                        Studios = season.Studios.ToArray(),
                        CommunityRating = season.TvDB.Rating?.ToFloat(10),
                    };
                    AddProviderIds(result.Item, season.Id, show.Id, season.AniDB.Id.ToString(), season.TvDB.Id.ToString(), season.Shoko.IDs.TMDB.FirstOrDefault().ToString());
                }
                else {
                    var premiereDate = show.SeasonList
                        .Select(s => s.AniDB.AirDate)
                        .Where(s => s != null)
                        .OrderBy(s => s)
                        .FirstOrDefault();
                    var endDate = show.SeasonList.Any(s => s.AniDB.EndDate == null) ? null : show.SeasonList
                        .Select(s => s.AniDB.AirDate)
                        .OrderBy(s => s)
                        .LastOrDefault();
                    result.Item = new Series {
                        Name = displayTitle,
                        OriginalTitle = alternateTitle,
                        Overview = Text.GetDescription(season),
                        PremiereDate = premiereDate,
                        ProductionYear = premiereDate?.Year,
                        EndDate = endDate,
                        Status = !endDate.HasValue || endDate.Value > DateTime.UtcNow ? SeriesStatus.Continuing : SeriesStatus.Ended,
                        Tags = show.Tags.ToArray(),
                        Genres = show.Genres.ToArray(),
                        Studios = show.Studios.ToArray(),
                        OfficialRating = season.AniDB.Restricted ? "XXX" : null,
                        CustomRating = season.AniDB.Restricted ? "XXX" : null,
                        CommunityRating = mergeFriendly ? season.TvDB.Rating.ToFloat(10) : season.AniDB.Rating.ToFloat(10),
                    };
                    AddProviderIds(result.Item, season.Id, show.Id, season.AniDB.Id.ToString());
                }

                result.HasMetadata = true;

                result.ResetPeople();
                foreach (var person in season.Staff)
                    result.AddPerson(person);

                return result;
            }
            catch (Exception ex) {
                Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
                Plugin.Instance.CaptureException(ex);
                return new MetadataResult<Series>();
            }
        }

        public static void AddProviderIds(IHasProviderIds item, string seriesId, string groupId = null, string anidbId = null, string tvdbId = null, string tmdbId = null)
        {
            // NOTE: These next two lines will remain here till _someone_ fix the series merging for providers other then TvDB and ImDB in Jellyfin.
            // NOTE: #2 Will fix this once JF 10.9 is out, as it contains a change that will help in this situation.
            if (string.IsNullOrEmpty(tvdbId))
                item.SetProviderId(MetadataProvider.Imdb, $"INVALID-BUT-DO-NOT-TOUCH:{seriesId}");

            var config = Plugin.Instance.Configuration;
            item.SetProviderId("Shoko Series", seriesId);
            if (!string.IsNullOrEmpty(groupId))
                item.SetProviderId("Shoko Group", groupId);
            if (config.AddAniDBId && !string.IsNullOrEmpty(anidbId) && anidbId != "0")
                item.SetProviderId("AniDB", anidbId);
            if (config.AddTvDBId &&!string.IsNullOrEmpty(tvdbId) && tvdbId != "0")
                item.SetProviderId(MetadataProvider.Tvdb, tvdbId);
            if (config.AddTMDBId &&!string.IsNullOrEmpty(tmdbId) && tmdbId != "0")
                item.SetProviderId(MetadataProvider.Tmdb, tmdbId);
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo info, CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<RemoteSearchResult>>(new List<RemoteSearchResult>());

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
            => HttpClientFactory.CreateClient().GetAsync(url, cancellationToken);
    }
}
