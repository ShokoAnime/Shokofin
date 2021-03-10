using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Utils;

using IResolverIgnoreRule = MediaBrowser.Controller.Resolvers.IResolverIgnoreRule;
using ILibraryManager = MediaBrowser.Controller.Library.ILibraryManager;
using SeriesType = Shokofin.API.Models.SeriesType;

namespace Shokofin.Providers
{
    public class MovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>, IResolverIgnoreRule
    {
        public string Name => "Shoko";
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<MovieProvider> _logger;

        private readonly ILibraryManager _library;

        public MovieProvider(IHttpClientFactory httpClientFactory, ILogger<MovieProvider> logger, ILibraryManager library)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _library = library;
        }

        public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
        {
            try
            {
                var result = new MetadataResult<Movie>();

                var includeGroup = Plugin.Instance.Configuration.BoxSetGrouping == Ordering.SeriesOrBoxSetGroupType.ShokoGroup;
                var (file, episode, series, group) = await DataFetcher.GetFileInfoByPath(info.Path, includeGroup, true);

                if (file == null) // if file is null then series and episode is also null.
                {
                    _logger.LogWarning($"Unable to find file info for path {info.Path}");
                    return result;
                }

                bool isMultiEntry = series.Shoko.Sizes.Total.Episodes > 1;
                int aniDBId = isMultiEntry ? episode.AniDB.ID : series.AniDB.ID;
                var tvdbId = (isMultiEntry ? episode?.TvDB == null ? null : episode.TvDB.ID.ToString() : series?.TvDBID);

                if (series.AniDB.Type != SeriesType.Movie)
                {
                    _logger.LogWarning($"File found, but not a movie! Skipping path {info.Path}");
                    return result;
                }

                var tags = await DataFetcher.GetTags(series.ID);
                var ( displayTitle, alternateTitle ) = Text.GetMovieTitles(series.AniDB.Titles, episode.AniDB.Titles, series.Shoko.Name, episode.Shoko.Name, info.MetadataLanguage);
                var rating = isMultiEntry ? episode.AniDB.Rating.ToFloat(10) : series.AniDB.Rating.ToFloat(10);

                result.Item = new Movie
                {
                    IndexNumber = Ordering.GetMovieIndexNumber(group, series, episode),
                    Name = displayTitle,
                    OriginalTitle = alternateTitle,
                    PremiereDate = episode.AniDB.AirDate,
                    // Use the file description if collection contains more than one movie, otherwise use the collection description.
                    Overview = Text.SummarySanitizer((isMultiEntry ? episode.AniDB.Description ?? series.AniDB.Description : series.AniDB.Description) ?? ""),
                    ProductionYear = episode.AniDB.AirDate?.Year,
                    Tags = tags,
                    CommunityRating = rating,
                };
                result.Item.SetProviderId("Shoko File", file.ID);
                result.Item.SetProviderId("Shoko Series", series.ID);
                result.Item.SetProviderId("Shoko Episode", episode.ID);
                if (aniDBId != 0)
                    result.Item.SetProviderId("AniDB", aniDBId.ToString());
                if (!string.IsNullOrEmpty(tvdbId))
                    result.Item.SetProviderId("Tvdb", tvdbId);
                result.HasMetadata = true;

                result.ResetPeople();
                foreach (var person in await DataFetcher.GetPeople(series.ID))
                    result.AddPerson(person);

                return result;
            }
            catch (Exception e)
            {
                _logger.LogError($"{e.Message}{Environment.NewLine}{e.StackTrace}");
                return new MetadataResult<Movie>();
            }
        }


        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
        {
            // Isn't called from anywhere. If it is called, I don't know from where.
            throw new NotImplementedException();
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
        }

        public bool ShouldIgnore(MediaBrowser.Model.IO.FileSystemMetadata fileInfo, BaseItem parent)
        {
            // Skip this handler if one of these requirements are met
            if (fileInfo == null || parent == null || fileInfo.IsDirectory || !fileInfo.Exists)
                return false;
            var libType = _library.GetInheritedContentType(parent);
            if (libType != "movies") {
                return false;
            }
            try {
                var path = System.IO.Path.Join(fileInfo.DirectoryName, fileInfo.FullName);
                var (file, episode, series, _group) = DataFetcher.GetFileInfoByPathSync(path);
                if (file == null)
                {
                    _logger.LogWarning($"Shoko Scanner... Unable to find series info for path {path}");
                    return false;
                }
                _logger.LogInformation($"Shoko Filter... Found series info for path {path}");
                if (series.AniDB.Type != SeriesType.Movie) {
                    return true;
                }
                var extraType = Ordering.GetExtraType(episode.AniDB);
                if (extraType != null) {
                    _logger.LogInformation($"Shoko Filter... File was not a 'normal' episode for path, skipping! {path}");
                    return true;
                }
                // Ignore everything except movies
                return false;
            }
            catch (System.Exception e)
            {
                if (!(e is System.Net.Http.HttpRequestException && e.Message.Contains("Connection refused")))
                    _logger.LogError(e, "Threw unexpectedly");
                return false;
            }
        }
    }
}
