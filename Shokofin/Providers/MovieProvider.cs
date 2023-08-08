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
    public class MovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>
    {
        public string Name => Plugin.MetadataProviderName;

        private readonly IHttpClientFactory HttpClientFactory;

        private readonly ILogger<MovieProvider> Logger;

        private readonly ShokoAPIManager ApiManager;

        public MovieProvider(IHttpClientFactory httpClientFactory, ILogger<MovieProvider> logger, ShokoAPIManager apiManager)
        {
            Logger = logger;
            HttpClientFactory = httpClientFactory;
            ApiManager = apiManager;
        }

        public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
        {
            try {
                var result = new MetadataResult<Movie>();

                var includeGroup = Plugin.Instance.Configuration.BoxSetGrouping == Ordering.GroupType.ShokoGroup;
                var config = Plugin.Instance.Configuration;
                Ordering.GroupFilterType? filterByType = config.BoxSetGrouping == Ordering.GroupType.ShokoGroup ? config.FilterOnLibraryTypes ? Ordering.GroupFilterType.Movies : Ordering.GroupFilterType.Default : null;
                var (file, series, group) = await ApiManager.GetFileInfoByPath(info.Path, filterByType);
                var episode = file?.EpisodeList.FirstOrDefault();

                // if file is null then series and episode is also null.
                if (file == null) {
                    Logger.LogWarning("Unable to find movie info for path {Path}", info.Path);
                    return result;
                }

                var collectionName = GetCollectionName(series, group, info.MetadataLanguage);
                var ( displayTitle, alternateTitle ) = Text.GetMovieTitles(series.AniDB.Titles, episode.AniDB.Titles, series.Shoko.Name, episode.Shoko.Name, info.MetadataLanguage);
                Logger.LogInformation("Found movie {EpisodeName} (File={FileId},Episode={EpisodeId},Series={SeriesId})", displayTitle, file.Id, episode.Id, series.Id);

                bool isMultiEntry = series.Shoko.Sizes.Total.Episodes > 1;
                bool isMainEntry = episode.AniDB.Type == API.Models.EpisodeType.Normal && episode.Shoko.Name.Trim() == "Complete Movie";
                var rating = isMultiEntry ? episode.AniDB.Rating.ToFloat(10) : series.AniDB.Rating.ToFloat(10);

                result.Item = new Movie {
                    IndexNumber = Ordering.GetMovieIndexNumber(group, series, episode),
                    Name = displayTitle,
                    OriginalTitle = alternateTitle,
                    PremiereDate = episode.AniDB.AirDate,
                    CollectionName = collectionName,
                    // Use the file description if collection contains more than one movie and the file is not the main entry, otherwise use the collection description.
                    Overview = (isMultiEntry && !isMainEntry ? Text.GetDescription(episode) : Text.GetDescription(series)),
                    ProductionYear = episode.AniDB.AirDate?.Year,
                    Tags = series.Tags.ToArray(),
                    Genres = series.Genres.ToArray(),
                    Studios = series.Studios.ToArray(),
                    CommunityRating = rating,
                };
                result.Item.SetProviderId("Shoko File", file.Id);
                result.Item.SetProviderId("Shoko Episode", episode.Id);
                result.Item.SetProviderId("Shoko Series", series.Id);
                if (config.AddAniDBId)
                    result.Item.SetProviderId("AniDB", series.AniDB.Id.ToString());

                result.HasMetadata = true;

                result.ResetPeople();
                foreach (var person in series.Staff)
                    result.AddPerson(person);

                return result;
            }
            catch (Exception ex) {
                Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
                Plugin.Instance.CaptureException(ex);
                return new MetadataResult<Movie>();
            }
        }

        private static string GetCollectionName(API.Info.SeasonInfo series, API.Info.ShowInfo group, string metadataLanguage)
        {
            return Plugin.Instance.Configuration.BoxSetGrouping switch {
                Ordering.GroupType.ShokoGroup =>
                    Text.GetSeriesTitle(group.DefaultSeason.AniDB.Titles, group.DefaultSeason.Shoko.Name, metadataLanguage),
                Ordering.GroupType.ShokoSeries =>
                    Text.GetSeriesTitle(series.AniDB.Titles, series.Shoko.Name, metadataLanguage),
                _ => null,
            };
        }


        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<RemoteSearchResult>>(new List<RemoteSearchResult>());

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
            => HttpClientFactory.CreateClient().GetAsync(url, cancellationToken);
    }
}
