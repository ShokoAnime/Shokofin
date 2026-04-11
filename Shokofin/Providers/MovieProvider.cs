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
using Shokofin.API;
using Shokofin.ExternalIds;
using Shokofin.Utils;

namespace Shokofin.Providers;

public class MovieProvider(IHttpClientFactory _httpClientFactory, ILogger<MovieProvider> _logger, ShokoApiManager _apiManager) : IRemoteMetadataProvider<Movie, MovieInfo>, IHasOrder {
    public string Name => Plugin.MetadataProviderName;

    public int Order => 0;

    public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken) {
        var trackerId = Plugin.Instance.Tracker.Add($"Providing info for Movie \"{info.Name}\". (Path=\"{info.Path}\")");
        try {
            var result = new MetadataResult<Movie>();
            var (fileInfo, seasonInfo, _) = await _apiManager.GetFileInfoByPath(info.Path).ConfigureAwait(false);
            var episodeInfo = fileInfo is { EpisodeList.Count: > 0 } ? fileInfo.EpisodeList[0].Episode : null;
            if (fileInfo == null || episodeInfo == null || seasonInfo == null) {
                _logger.LogWarning("Unable to find movie info for path {Path}", info.Path);
                return result;
            }

            var (displayTitle, alternateTitle) = TextUtility.GetMovieTitles(episodeInfo, seasonInfo, info.MetadataLanguage);
            if (string.IsNullOrEmpty(displayTitle))
                displayTitle = episodeInfo.Id[0] == IdPrefix.TmdbMovie
                    ? episodeInfo.Title
                    : TextUtility.IgnoredSubTitles.Contains(episodeInfo.Title)
                        ? seasonInfo.Title
                        : $"{seasonInfo.Title}: {episodeInfo.Title}";

            var rating = seasonInfo.IsMultiEntry
                ? episodeInfo.CommunityRating.ToFloat(10)
                : seasonInfo.CommunityRating.ToFloat(10);

            _logger.LogInformation("Found movie {EpisodeName} (File={FileId},Episode={EpisodeId},Season={SeasonId},ExtraSeasons={ExtraIds})", displayTitle, fileInfo.Id, episodeInfo.Id, seasonInfo.Id, seasonInfo.ExtraIds);

            result.Item = new Movie {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                PremiereDate = episodeInfo.AiredAt,
                Overview = TextUtility.GetMovieDescription(episodeInfo, seasonInfo, info.MetadataLanguage),
                ProductionYear = episodeInfo.AiredAt?.Year,
                Tags = [.. episodeInfo.Tags],
                Genres = [.. episodeInfo.Genres],
                Studios = [.. episodeInfo.Studios],
                ProductionLocations = TagFilter.GetProductionLocations(episodeInfo),
                OfficialRating = ContentRating.GetContentRating(episodeInfo, info.MetadataCountryCode),
                CommunityRating = rating,
            };

            result.Item.SetProviderId(ShokoInternalId.Name, fileInfo.InternalId);
            result.Item.SetProviderId(ProviderNames.Shoko, ShokoExternalUrlHandler.GetFileInfoUrls(fileInfo));
            result.Item.SetProviderId(ProviderNames.ShokoFile, fileInfo.Id);
            result.Item.SetProviderId(ProviderNames.ShokoSeries, fileInfo.SeriesId);
            result.Item.SetProviderId(ProviderNames.ShokoEpisode, episodeInfo.Id);
            if (Plugin.Instance.Configuration.AddAniDBId && seasonInfo.AnidbAnimeId is { Length: > 0 } anidbAnimeId)
                result.Item.SetProviderId(ProviderNames.Anidb, anidbAnimeId);
            if (Plugin.Instance.Configuration.AddTMDBId && episodeInfo.TmdbMovieId is { Length: > 0 } tmdbMovieId)
                result.Item.SetProviderId(MetadataProvider.Tmdb, tmdbMovieId);

            result.HasMetadata = true;

            result.ResetPeople();
            foreach (var person in episodeInfo.Staff)
                result.AddPerson(person);

            return result;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Threw unexpectedly while refreshing {Path}; {Message}", info.Path, ex.Message);
            return new MetadataResult<Movie>();
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
        => Task.FromResult<IEnumerable<RemoteSearchResult>>([]);

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => await _httpClientFactory.CreateClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
}
