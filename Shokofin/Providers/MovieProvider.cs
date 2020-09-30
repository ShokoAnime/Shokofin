using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

                // TO-DO Check if it can be written in a better way. Parent directory + File Name
                var filename = Path.Join(
                        Path.GetDirectoryName(info.Path)?.Split(Path.DirectorySeparatorChar).LastOrDefault(),
                        Path.GetFileName(info.Path));

                _logger.LogInformation($"Shoko Scanner... Getting movie ID ({filename})");

                var apiResponse = await ShokoAPI.GetFilePathEndsWith(filename);
                var file = apiResponse?.FirstOrDefault();
                var fileId = file?.ID.ToString();
                var seriesIds = file?.SeriesIDs.FirstOrDefault();
                var seriesId = seriesIds?.SeriesID.ID.ToString();
                var episodeIds = seriesIds?.EpisodeIDs?.FirstOrDefault();
                var episodeId = episodeIds?.ID.ToString();

                if (string.IsNullOrEmpty(episodeId) || string.IsNullOrEmpty(seriesId))
                {
                    _logger.LogInformation($"Shoko Scanner... File not found! ({filename})");
                    return result;
                }

                _logger.LogInformation($"Shoko Scanner... Getting movie metadata ({filename} - {episodeId})");

                var seriesAniDB = await ShokoAPI.GetSeriesAniDb(seriesId);
                var series = await ShokoAPI.GetSeries(seriesId);
                var episodeAniDB = await ShokoAPI.GetEpisodeAniDb(episodeId);
                var episode = await ShokoAPI.GetEpisode(episodeId);
                bool isMultiEntry = series.Sizes.Total.Episodes > 1;
                int aniDBId = (isMultiEntry ? episodeIds?.AniDB : seriesIds?.SeriesID.AniDB) ?? 0;
                int tvdbId = (isMultiEntry ? episodeIds?.TvDB?.FirstOrDefault() : seriesIds?.SeriesID.TvDB?.FirstOrDefault()) ?? 0;

                if (seriesAniDB?.SeriesType != "0")
                {
                    _logger.LogInformation($"Shoko Scanner... File found, but not a movie! Skipping.");
                    return result;
                }

                var tags = await ShokoAPI.GetSeriesTags(seriesId, Helper.GetFlagFilter());
                var ( displayTitle, alternateTitle ) = Helper.GetMovieTitles(seriesAniDB.Titles, episodeAniDB.Titles, series.Name, episode.Name, Plugin.Instance.Configuration.TitleMainType, Plugin.Instance.Configuration.TitleAlternateType, info.MetadataLanguage);
                float comRat = isMultiEntry ? (float)((episodeAniDB.Rating.Value * 10) / episodeAniDB.Rating.MaxValue) : (float)((seriesAniDB.Rating.Value * 10) / seriesAniDB.Rating.MaxValue);
                ExtraType? extraType = Helper.GetExtraType(episodeAniDB);

                result.Item = new Movie
                {
                    IndexNumber = Helper.GetIndexNumber(series, episodeAniDB),
                    Name = displayTitle,
                    OriginalTitle = alternateTitle,
                    PremiereDate = isMultiEntry ? episodeAniDB.AirDate : seriesAniDB.AirDate,
                    // Use the file description if collection contains more than one movie, otherwise use the collection description.
                    Overview = Helper.SummarySanitizer((isMultiEntry ? episodeAniDB.Description : seriesAniDB.Description) ?? ""),
                    ProductionYear = isMultiEntry ? episodeAniDB.AirDate?.Year : seriesAniDB.AirDate?.Year,
                    ExtraType = extraType,                    
                    Tags = tags?.Select(tag => tag.Name).ToArray() ?? new string[0],
                    CommunityRating = comRat,
                };
                result.Item.SetProviderId("Shoko File", fileId);
                result.Item.SetProviderId("Shoko Series", seriesId);
                result.Item.SetProviderId("Shoko Episode", episodeId);
                if (aniDBId != 0) result.Item.SetProviderId("AniDB", aniDBId.ToString());
                if (tvdbId != 0) result.Item.SetProviderId("Tvdb", tvdbId.ToString());
                result.HasMetadata = true;

                result.ResetPeople();
                var roles = await ShokoAPI.GetSeriesCast(seriesId);
                foreach (var role in roles)
                {
                    result.AddPerson(new PersonInfo
                    {
                        Type = PersonType.Actor,
                        Name = role.Staff.Name,
                        Role = role.Character.Name,
                        ImageUrl = Helper.GetImageUrl(role.Staff.Image)
                    });
                }

                return result;
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message);
                _logger.LogError(e.InnerException?.StackTrace ?? e.StackTrace);
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