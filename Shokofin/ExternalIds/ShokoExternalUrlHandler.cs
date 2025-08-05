using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Extensions;
using Shokofin.Utils;

namespace Shokofin.ExternalIds;

public class ShokoExternalUrlHandler(ILogger<ShokoExternalUrlHandler> logger, ShokoIdLookup lookup, ShokoApiManager apiManager, UsageTracker tracker) : IExternalUrlProvider {

    #region IExternalUrlProvider Implementation

    private readonly Queue<string> _nextNames = new();

    private readonly object _lock = new();

    string IExternalUrlProvider.Name => _nextNames.TryDequeue(out var name) ? name : "Shoko";

    /// <inheritdoc/>
    IEnumerable<string> IExternalUrlProvider.GetExternalUrls(BaseItem item)
    {
        if (!lookup.IsEnabledForItem(item))
            yield break;

        var list = GetExternalUrls(item);
        lock (_lock) {
            _nextNames.Clear();
            foreach (var (name, url1) in list) {
                _nextNames.Enqueue(name);
                yield return url1;
            }
        }
    }

    #endregion

    private IReadOnlyCollection<(string, string)> GetExternalUrls(BaseItem item)
        => (item, Plugin.Instance.Configuration.DisplayMoreExternalUrls) switch {
            (BoxSet boxSet, _) => [..GetCollectionUrls(boxSet).Distinct()],
            (Person person, _) => [..GetPersonUrls(person).Distinct()],

            (Series series, false) => [..GetSeriesUrls(series).Distinct()],
            (Season season, false) => [..GetSeasonUrls(season).Distinct()],
            (Episode episode, false) => [..GetEpisodeUrls(episode).Distinct()],
            (Movie movie, false) => [..GetMovieUrls(movie).Distinct()],
            (Video video, false) => [..GetVideoUrls(video).Distinct()],

            (Series series, true) => GetSeriesUrlsAsync(series).ConfigureAwait(false).GetAwaiter().GetResult(),
            (Season season, true) => GetSeasonUrlsAsync(season).ConfigureAwait(false).GetAwaiter().GetResult(),
            (Episode episode, true) => GetEpisodeUrlsAsync(episode).ConfigureAwait(false).GetAwaiter().GetResult(),
            (Video video, true) => GetVideoUrlsAsync(video).ConfigureAwait(false).GetAwaiter().GetResult(),
            _ => [],
        };

    #region Sync

    private IEnumerable<(string Name, string Url)> GetCollectionUrls(BoxSet boxSet) {
        var config = Plugin.Instance.Configuration;
        var url = config.WebUrl;

        if (boxSet.Path.TryGetAttributeValue(ProviderNames.ShokoCollectionForGroup, out var collectionId)) {
            yield return ($"{ProviderNames.ShokoGroup} (g{collectionId})", $"{url}/collection/group/{collectionId}");
        }
        if (boxSet.Path.TryGetAttributeValue(ProviderNames.ShokoCollectionForSeries, out var seasonId)) {
            if (seasonId[0] is not IdPrefix.TmdbMovie and not IdPrefix.TmdbMovieCollection and not IdPrefix.TmdbShow)
                yield return ($"{ProviderNames.ShokoSeries} (s{seasonId})", $"{url}/collection/series/{seasonId}");
        }
    }

    private IEnumerable<(string, string)> GetSeriesUrls(Series series) {
        var url = Plugin.Instance.Configuration.WebUrl;
        if (series.TryGetProviderId(ProviderNames.Anidb, out var animeId))
            yield return ($"{ProviderNames.Anidb} (a{animeId})", $"https://anidb.net/anime/{animeId}");
        if (series.TryGetProviderId(ProviderNames.ShokoGroup, out var groupId))
            yield return ($"{ProviderNames.ShokoGroup} (g{groupId})", $"{url}/collection/group/{groupId}");
        if (series.TryGetProviderId(ProviderNames.ShokoSeries, out var seriesId))
            yield return ($"{ProviderNames.ShokoSeries} (s{seriesId})", $"{url}/collection/series/{seriesId}");
    }

    private IEnumerable<(string, string)> GetSeasonUrls(Season season) {
        var url = Plugin.Instance.Configuration.WebUrl;
        if (season.TryGetProviderId(ProviderNames.Anidb, out var animeId))
            yield return ($"{ProviderNames.Anidb} (a{animeId})", $"https://anidb.net/anime/{animeId}");
        if (season.TryGetProviderId(ProviderNames.ShokoGroup, out var groupId))
            yield return ($"{ProviderNames.ShokoGroup} (g{groupId})", $"{url}/collection/group/{groupId}");
        if (season.TryGetProviderId(ProviderNames.ShokoSeries, out var seriesId))
            yield return ($"{ProviderNames.ShokoSeries} (s{seriesId})", $"{url}/collection/series/{seriesId}");
    }

    private IEnumerable<(string Name, string Url)> GetEpisodeUrls(Episode episode) {
        if (episode.TryGetProviderId(ProviderNames.Anidb, out var anidbEpisodeId))
            yield return ($"{ProviderNames.Anidb} (e{anidbEpisodeId})",$"https://anidb.net/episode/{anidbEpisodeId}");
        if (episode.IsMissingEpisode || string.IsNullOrEmpty(episode.Path)) {
            if (episode.TryGetProviderId(ProviderNames.ShokoEpisode, out var shokoEpisodeId) && (episode.Series?.TryGetProviderId(ProviderNames.ShokoSeries, out var shokoSeriesId) ?? false))
                yield return ($"{ProviderNames.ShokoEpisode} (e{shokoEpisodeId})", $"{Plugin.Instance.Configuration.WebUrl}/collection/series/{shokoSeriesId}/episodes?episodeId={shokoEpisodeId}");
        }
        else {
            foreach (var tuple in GetVideoUrls(episode))
                yield return tuple;
        }
    }

    private IEnumerable<(string Name, string Url)> GetMovieUrls(Movie movie) {
        if (movie.TryGetProviderId(ProviderNames.Anidb, out var animeId))
            yield return ($"{ProviderNames.Anidb} (a{animeId})", $"https://anidb.net/anime/{animeId}");
        foreach (var tuple in GetVideoUrls(movie))
            yield return tuple;
    }

    private IEnumerable<(string Name, string Url)> GetVideoUrls(Video video) {
        var url = Plugin.Instance.Configuration.WebUrl;
        using (tracker.Enter("Get External Urls for Video (Sync)")) {
            if (lookup.TryGetFileAndSeriesIdFor(video, out var fileId, out var seriesId)) {
                yield return ($"{ProviderNames.ShokoSeries} (s{seriesId})", $"{url}/collection/series/{seriesId}");
                yield return ($"{ProviderNames.ShokoFile} (f{fileId})", $"{url}/collection/series/{seriesId}/files?fileId={fileId}");
            }
            if (lookup.TryGetEpisodeIdsFor(video, out var episodeIds)) {
                foreach (var episodeId in episodeIds) {
                    switch (episodeId[0]) {
                        case IdPrefix.TmdbMovie:
                            yield return ($"{ProviderNames.Tmdb} (m{episodeId[1..]})", $"https://www.themoviedb.org/movie/{episodeId[1..]}");
                            break;
                        case IdPrefix.TmdbShow:
                            // TMDb doesn't have a episode page using the episode id, they only have series id + season number + episode number.
                            break;
                        default:
                            yield return ($"{ProviderNames.ShokoEpisode} (e{episodeId})", $"{url}/collection/series/{seriesId}/episodes?episodeId={episodeId}");
                            break;
                    }
                }
            }
        }
    }

    private IEnumerable<(string Name, string Url)> GetPersonUrls(Person person) {
        if (person.TryGetProviderId(ProviderNames.Anidb, out var creatorId)) {
            yield return ($"{ProviderNames.Anidb} (c{creatorId})", $"https://anidb.net/creator/{creatorId}");
        }
    }

    #endregion

    #region Async

    private async Task<IReadOnlyCollection<(string, string)>> GetSeriesUrlsAsync(Series series) {
        if (!series.TryGetSeasonId(out var seasonId))
            return [];

        var url = Plugin.Instance.Configuration.WebUrl;
        var result = new List<(string, string)>();
        using (tracker.Enter($"Providing external URLs for Series \"{series.Name}\". (Path=\"{series.Path}\")")) {
            if (await apiManager.GetShowInfoBySeasonId(seasonId).ConfigureAwait(false) is not { } showInfo)
                return result;

            foreach (var shokoInfo in showInfo.ShokoSeries)
                result.Add(($"{ProviderNames.Shoko} (s{shokoInfo.ShokoSeriesId}) (g{shokoInfo.ShokoGroupId})", $"{url}/collection/series/{shokoInfo.ShokoSeriesId}"));

            foreach (var anidbInfo in showInfo.AnidbAnime)
                result.Add(($"{ProviderNames.Anidb} (a{anidbInfo.AnidbAnimeId})", $"https://anidb.net/anime/{anidbInfo.AnidbAnimeId}"));

            foreach (var tmdbInfo in showInfo.TmdbShows) {
                if (tmdbInfo.UsesAlternateOrdering)
                    result.Add((
                        $"{ProviderNames.Tmdb} (tv{tmdbInfo.TmdbShowId} > g{tmdbInfo.TmdbAlternateOrderingId})",
                        $"https://www.themoviedb.org/tv/{tmdbInfo.TmdbShowId}/episode_group/{tmdbInfo.TmdbAlternateOrderingId}"
                    ));
                else
                    result.Add((
                        $"{ProviderNames.Tmdb} (tv{tmdbInfo.TmdbShowId})",
                        $"https://www.themoviedb.org/tv/{tmdbInfo.TmdbShowId}"
                    ));
                if (!string.IsNullOrEmpty(tmdbInfo.TvdbShowId))
                    result.Add(($"{ProviderNames.Tvdb} (tv{tmdbInfo.TvdbShowId})", $"https://thetvdb.com/?tab=series&id={tmdbInfo.TvdbShowId}"));
            }

            foreach (var tmdbInfo in showInfo.TmdbMovies) {
                result.Add(($"{ProviderNames.Tmdb} (m{tmdbInfo.TmdbMovieId})", $"https://www.themoviedb.org/movie/{tmdbInfo.TmdbMovieId}"));
                if (!string.IsNullOrEmpty(tmdbInfo.TmdbMovieCollectionId))
                    result.Add(($"{ProviderNames.Tmdb} (c{tmdbInfo.TmdbMovieCollectionId})", $"https://www.themoviedb.org/collection/{tmdbInfo.TmdbMovieCollectionId}"));
            }
        }

        return result.Distinct().ToList();
    }

    private async Task<IReadOnlyCollection<(string, string)>> GetSeasonUrlsAsync(Season season) {
        if (!season.IndexNumber.HasValue)
            return [];

        var series = season.Series;
        if (!series.TryGetSeasonId(out var seasonId))
            return [];

        var seasonNumber = season.IndexNumber.Value;
        var url = Plugin.Instance.Configuration.WebUrl;
        var result = new List<(string, string)>();
        using (tracker.Enter($"Providing info for Season \"{season.Name}\". (Path=\"{season.Path}\",Series=\"{seasonId}\",Season={seasonNumber})")) {
            var showInfo = await apiManager.GetShowInfoBySeasonId(seasonId).ConfigureAwait(false);
            if (showInfo == null) {
                logger.LogWarning("Unable to find show info for Season {SeasonNumber}. (MainSeason={MainSeasonId})", seasonNumber, seasonId);
                return result;
            }

            var seasonInfo = showInfo.GetSeasonInfoBySeasonNumber(seasonNumber);
            if (seasonInfo == null || !showInfo.TryGetBaseSeasonNumberForSeasonInfo(seasonInfo, out var _)) {
                logger.LogWarning("Unable to find series info for Season {SeasonNumber}. (MainSeason={MainSeasonId})", seasonNumber, seasonId);
                return result;
            }

            foreach (var shokoInfo in seasonInfo.ShokoSeries)
                result.Add(($"{ProviderNames.Shoko} (s{shokoInfo.ShokoSeriesId}) (g{shokoInfo.ShokoGroupId})", $"{url}/collection/series/{shokoInfo.ShokoSeriesId}"));

            foreach (var anidbInfo in seasonInfo.AnidbAnime)
                result.Add(($"{ProviderNames.Anidb} (a{anidbInfo.AnidbAnimeId})", $"https://anidb.net/anime/{anidbInfo.AnidbAnimeId}"));

            foreach (var tmdbInfo in seasonInfo.TmdbSeasons) {
                if (tmdbInfo.UsesAlternateOrdering)
                    result.Add((
                        $"{ProviderNames.Tmdb} (tv{tmdbInfo.TmdbShowId} > g{tmdbInfo.TmdbAlternateOrderingId} > S{tmdbInfo.SeasonNumber})",
                        $"https://www.themoviedb.org/tv/{tmdbInfo.TmdbShowId}/episode_group/{tmdbInfo.TmdbAlternateOrderingId}/group/{tmdbInfo.TmdbSeasonId}"
                    ));
                else
                    result.Add((
                        $"{ProviderNames.Tmdb} (s{tmdbInfo.TmdbSeasonId}) (tv{tmdbInfo.TmdbShowId} > S{tmdbInfo.SeasonNumber})",
                        $"https://www.themoviedb.org/tv/{tmdbInfo.TmdbShowId}/season/{tmdbInfo.SeasonNumber}"
                    ));
            }

            foreach (var tmdbInfo in seasonInfo.TmdbMovies) {
                result.Add(($"{ProviderNames.Tmdb} (m{tmdbInfo.TmdbMovieId})", $"https://www.themoviedb.org/movie/{tmdbInfo.TmdbMovieId}"));
                if (!string.IsNullOrEmpty(tmdbInfo.TmdbMovieCollectionId))
                    result.Add(($"{ProviderNames.Tmdb} (c{tmdbInfo.TmdbMovieCollectionId})", $"https://www.themoviedb.org/collection/{tmdbInfo.TmdbMovieCollectionId}"));
            }
        }

        return result.Distinct().ToList();
    }

    private Task<IReadOnlyCollection<(string Name, string Url)>> GetEpisodeUrlsAsync(Episode episode)
        => episode.IsMissingEpisode || string.IsNullOrEmpty(episode.Path)
            ? GetMissingEpisodeUrlsAsync(episode)
            : GetVideoUrlsAsync(episode);

    private async Task<IReadOnlyCollection<(string Name, string Url)>> GetMissingEpisodeUrlsAsync(Episode episode) {
        var result = new List<(string, string)>();
        if (!episode.TryGetProviderId(ProviderNames.ShokoEpisode, out var shokoEpisodeId)) {
            logger.LogWarning("Unable to find Shoko Episode Id for Virtual Episode {EpisodePath}.", episode.Path);
            return result;
        }

        var url = Plugin.Instance.Configuration.WebUrl;
        if (await apiManager.GetEpisodeInfo(shokoEpisodeId) is not { } episodeInfo) {
            logger.LogWarning("Unable to find episode info for Virtual Episode {EpisodePath}.", episode.Path);
            return result;
        }

        foreach (var shokoInfo in episodeInfo.ShokoEpisodes)
            result.Add(($"{ProviderNames.Shoko} (e{shokoInfo.ShokoEpisodeId}) (s{shokoInfo.ShokoSeriesId})", $"{url}/collection/series/{shokoInfo.ShokoSeriesId}/episodes?episodeId={shokoInfo.ShokoEpisodeId}"));

        foreach (var anidbInfo in episodeInfo.AnidbEpisodes)
            result.Add(($"{ProviderNames.Anidb} (e{anidbInfo.AnidbAnimeId}) (a{anidbInfo.AnidbAnimeId} > {anidbInfo.GetEpisodeNumberText()})", $"https://anidb.net/episode/{anidbInfo.AnidbAnimeId}"));

        foreach (var tmdbInfo in episodeInfo.TmdbEpisodes) {
            if (tmdbInfo.UsesAlternateOrdering)
                result.Add((
                    $"{ProviderNames.Tmdb} (e{tmdbInfo.TmdbEpisodeId}) (tv{tmdbInfo.TmdbShowId} > g{tmdbInfo.TmdbAlternateOrderingId} > S{tmdbInfo.SeasonNumber}E{tmdbInfo.EpisodeNumber})",
                    $"https://www.themoviedb.org/tv/{tmdbInfo.TmdbShowId}/season/{tmdbInfo.OriginalSeasonNumber}episode/{tmdbInfo.OriginalEpisodeNumber}"
                ));
            else
                result.Add((
                    $"{ProviderNames.Tmdb} (e{tmdbInfo.TmdbEpisodeId}) (tv{tmdbInfo.TmdbShowId} > S{tmdbInfo.SeasonNumber}E{tmdbInfo.EpisodeNumber})",
                    $"https://www.themoviedb.org/tv/{tmdbInfo.TmdbShowId}/season/{tmdbInfo.SeasonNumber}episode/{tmdbInfo.EpisodeNumber}"
                ));
            if (!string.IsNullOrEmpty(tmdbInfo.TvdbEpisodeId))
                result.Add(($"{ProviderNames.Tvdb} (e{tmdbInfo.TmdbEpisodeId})", $"https://thetvdb.com/?tab=episode&id={tmdbInfo.TmdbEpisodeId}"));
        }

        foreach (var tmdbInfo in episodeInfo.TmdbMovies) {
            result.Add(($"{ProviderNames.Tmdb} (m{tmdbInfo.TmdbMovieId})", $"https://www.themoviedb.org/movie/{tmdbInfo.TmdbMovieId}"));
            if (!string.IsNullOrEmpty(tmdbInfo.TmdbMovieCollectionId))
                result.Add(($"{ProviderNames.Tmdb} (c{tmdbInfo.TmdbMovieCollectionId})", $"https://www.themoviedb.org/collection/{tmdbInfo.TmdbMovieCollectionId}"));
        }

        return result.Distinct().ToList();
    }

    private async Task<IReadOnlyCollection<(string Name, string Url)>> GetVideoUrlsAsync(Video video) {
        var result = new List<(string, string)>();
        using (tracker.Enter("Get External Urls for Video (Async)")) {
            if (!lookup.TryGetFileAndSeriesIdFor(video, out var fileId, out var seriesId))
                return result;

            var url = Plugin.Instance.Configuration.WebUrl;
            result.Add(($"{ProviderNames.Shoko} (f{fileId})", $"{url}/collection/series/{seriesId}/files?fileId={fileId}"));
            result.Add(($"{ProviderNames.Shoko} (s{seriesId})", $"{url}/collection/series/{seriesId}"));
            if (await apiManager.GetFileInfo(fileId, seriesId).ConfigureAwait(false) is not { } fileInfo) {
                logger.LogWarning("Unable to find file info for Video {VideoId}. (File={FileId},Series={SeriesId})", video.Path, fileId, seriesId);
                return result;
            }

            foreach (var (episodeInfo, _, _) in fileInfo.EpisodeList) {
                foreach (var shokoInfo in episodeInfo.ShokoEpisodes)
                    result.Add(($"{ProviderNames.Shoko} (e{shokoInfo.ShokoEpisodeId}) (s{shokoInfo.ShokoSeriesId})", $"{url}/collection/series/{shokoInfo.ShokoSeriesId}/episodes?episodeId={shokoInfo.ShokoEpisodeId}"));

                foreach (var anidbInfo in episodeInfo.AnidbEpisodes)
                    result.Add(($"{ProviderNames.Anidb} (e{anidbInfo.AnidbAnimeId}) (a{anidbInfo.AnidbAnimeId} > {anidbInfo.GetEpisodeNumberText()})", $"https://anidb.net/episode/{anidbInfo.AnidbAnimeId}"));

                foreach (var tmdbInfo in episodeInfo.TmdbEpisodes) {
                    if (tmdbInfo.UsesAlternateOrdering)
                        result.Add((
                            $"{ProviderNames.Tmdb} (e{tmdbInfo.TmdbEpisodeId}) (tv{tmdbInfo.TmdbShowId} > g{tmdbInfo.TmdbAlternateOrderingId} > S{tmdbInfo.SeasonNumber}E{tmdbInfo.EpisodeNumber})",
                            $"https://www.themoviedb.org/tv/{tmdbInfo.TmdbShowId}/season/{tmdbInfo.OriginalSeasonNumber}episode/{tmdbInfo.OriginalEpisodeNumber}"
                        ));
                    else
                        result.Add((
                            $"{ProviderNames.Tmdb} (e{tmdbInfo.TmdbEpisodeId}) (tv{tmdbInfo.TmdbShowId} > S{tmdbInfo.SeasonNumber}E{tmdbInfo.EpisodeNumber})",
                            $"https://www.themoviedb.org/tv/{tmdbInfo.TmdbShowId}/season/{tmdbInfo.SeasonNumber}episode/{tmdbInfo.EpisodeNumber}"
                        ));
                    if (!string.IsNullOrEmpty(tmdbInfo.TvdbEpisodeId))
                        result.Add(($"{ProviderNames.Tvdb} (e{tmdbInfo.TmdbEpisodeId})", $"https://thetvdb.com/?tab=episode&id={tmdbInfo.TmdbEpisodeId}"));
                }

                foreach (var tmdbInfo in episodeInfo.TmdbMovies) {
                    result.Add(($"{ProviderNames.Tmdb} (m{tmdbInfo.TmdbMovieId})", $"https://www.themoviedb.org/movie/{tmdbInfo.TmdbMovieId}"));
                    if (!string.IsNullOrEmpty(tmdbInfo.TmdbMovieCollectionId))
                        result.Add(($"{ProviderNames.Tmdb} (c{tmdbInfo.TmdbMovieCollectionId})", $"https://www.themoviedb.org/collection/{tmdbInfo.TmdbMovieCollectionId}"));
                }
            }
        }

        return result.Distinct().ToList();
    }


    #endregion
}
