using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using EpisodeType = Shokofin.API.Models.Episode.EpisodeType;

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
                var allIds = apiResponse.FirstOrDefault()?.SeriesIDs.FirstOrDefault();
                var episodeIds = allIds?.EpisodeIDs?.FirstOrDefault();
                string seriesId = allIds?.SeriesID.ID.ToString();
                string episodeId = episodeIds?.ID.ToString();

                if (string.IsNullOrEmpty(episodeId) || string.IsNullOrEmpty(seriesId))
                {
                    _logger.LogInformation($"Shoko Scanner... File not found! ({filename})");
                    return result;
                }

                _logger.LogInformation($"Shoko Scanner... Getting movie metadata ({filename} - {episodeId})");

                var seriesAniDB = await ShokoAPI.GetSeriesAniDb(seriesId);
                var series = await ShokoAPI.GetSeries(seriesId);
                var episodeAniDB = await ShokoAPI.GetEpisodeAniDb(episodeId);
                bool isMultiEntry = series.Sizes.Total.Episodes > 1;
                int tvdbId = (isMultiEntry ? episodeIds.TvDB?.FirstOrDefault() : allIds?.SeriesID.TvDB?.FirstOrDefault()) ?? 0;

                if (seriesAniDB?.SeriesType != "0")
                {
                    _logger.LogInformation($"Shoko Scanner... File found, but not a movie! Skipping.");
                    return result;
                }

                var ( displayTitle, alternateTitle ) = Helper.GetFullTitles(seriesAniDB.Titles, episodeAniDB.Titles, seriesAniDB.Title, Plugin.Instance.Configuration.TitleMainType, Plugin.Instance.Configuration.TitleAlternateType, info.MetadataLanguage);
                var tags = await ShokoAPI.GetSeriesTags(seriesId, Helper.GetFlagFilter());
                // Use the file description if collection contains more than one movie, otherwise use the collection description.
                string description = (isMultiEntry ? episodeAniDB.Description : seriesAniDB.Description) ?? "";
                float comRat = isMultiEntry ? (float)((episodeAniDB.Rating.Value * 10) / episodeAniDB.Rating.MaxValue) : (float)((seriesAniDB.Rating.Value * 10) / seriesAniDB.Rating.MaxValue);
                ExtraType? extraType = GetExtraType(episodeAniDB.Type);

                result.Item = new Movie
                {
                    IndexNumber = episodeAniDB.EpisodeNumber,
                    Name = displayTitle,
                    OriginalTitle = alternateTitle,
                    PremiereDate = isMultiEntry ? episodeAniDB.AirDate : seriesAniDB.AirDate,
                    Overview = Helper.SummarySanitizer(description),
                    ProductionYear = isMultiEntry ? episodeAniDB.AirDate?.Year : seriesAniDB.AirDate?.Year,
                    ExtraType = extraType,
                    Tags = tags?.Select(tag => tag.Name).ToArray() ?? new string[0],
                    CommunityRating = comRat,
                };
                result.Item.SetProviderId("Shoko Series", seriesId);
                result.Item.SetProviderId("Shoko Episode", episodeId);
                result.Item.SetProviderId("AniDB", allIds?.SeriesID.AniDB.ToString());
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

        private ExtraType? GetExtraType(EpisodeType type)
        {
            switch (type)
            {
                case EpisodeType.Episode:
                    return null;
                case EpisodeType.Trailer:
                    return ExtraType.Trailer;
                case EpisodeType.Special:
                    return ExtraType.Scene;
                default:
                    return ExtraType.Unknown;
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