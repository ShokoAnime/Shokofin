using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.Utils;

namespace Shokofin.Providers
{
    public class MovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>
    {
        public string Name => "Shoko";
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<MovieProvider> _logger;

        public MovieProvider(IHttpClientFactory httpClientFactory, ILogger<MovieProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }


        public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
        {
            try
            {
                var result = new MetadataResult<Movie>();

                var (id, file, episode, series, _group) = await DataUtil.GetFileInfoByPath(info.Path);

                if (file == null) // if file is null then series and episode is also null.
                {
                    _logger.LogWarning($"Unable to find file info for path {info.Path}");
                    return result;
                }

                bool isMultiEntry = series.Shoko.Sizes.Total.Episodes > 1;
                int aniDBId = isMultiEntry ? episode.AniDB.ID : series.AniDB.ID;
                var tvdbId = (isMultiEntry ? episode?.TvDB == null ? null : episode.TvDB.ID.ToString() : series?.TvDBID);

                if (series.AniDB.Type != "Movie")
                {
                    _logger.LogWarning($"File found, but not a movie! Skipping path {id}");
                    return result;
                }

                var extraType = OrderingUtil.GetExtraType(episode.AniDB);
                if (extraType != null)
                {
                    _logger.LogWarning($"File found, but not a movie! Skipping path {id}");
                    return result;
                }

                var tags = await DataUtil.GetTags(series.ID);
                var ( displayTitle, alternateTitle ) = TextUtil.GetMovieTitles(series.AniDB.Titles, episode.AniDB.Titles, series.Shoko.Name, episode.Shoko.Name, info.MetadataLanguage);
                var rating = DataUtil.GetRating(isMultiEntry ? episode.AniDB.Rating : series.AniDB.Rating);

                result.Item = new Movie
                {
                    IndexNumber = OrderingUtil.GetIndexNumber(series, episode),
                    Name = displayTitle,
                    OriginalTitle = alternateTitle,
                    PremiereDate = episode.AniDB.AirDate,
                    // Use the file description if collection contains more than one movie, otherwise use the collection description.
                    Overview = TextUtil.SummarySanitizer((isMultiEntry ? episode.AniDB.Description ?? series.AniDB.Description : series.AniDB.Description) ?? ""),
                    ProductionYear = episode.AniDB.AirDate?.Year,
                    ExtraType = extraType,
                    Tags = tags,
                    CommunityRating = rating,
                };
                result.Item.SetProviderId("Shoko File", file.ID);
                result.Item.SetProviderId("Shoko Series", series.ID);
                result.Item.SetProviderId("Shoko Episode", episode.ID);
                if (aniDBId != 0) result.Item.SetProviderId("AniDB", aniDBId.ToString());
                if (!string.IsNullOrEmpty(tvdbId)) result.Item.SetProviderId("Tvdb", tvdbId);
                result.HasMetadata = true;

                result.ResetPeople();
                foreach (var person in await DataUtil.GetPeople(series.ID))
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
    }
}
