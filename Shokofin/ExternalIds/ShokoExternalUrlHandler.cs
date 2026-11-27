using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Shokofin.API;
using Shokofin.Extensions;

using DescriptionProvider = Shokofin.Utils.TextUtility.DescriptionProvider;

namespace Shokofin.ExternalIds;

public class ShokoExternalUrlHandler(ShokoIdLookup lookup) : IExternalUrlProvider {

    #region IExternalUrlProvider Implementation

    private readonly Queue<string> _nextNames = new();

    private readonly object _lock = new();

    string IExternalUrlProvider.Name => _nextNames.TryDequeue(out var name) ? name : "Shoko";

    /// <inheritdoc/>
    IEnumerable<string> IExternalUrlProvider.GetExternalUrls(BaseItem item) {
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

    #region Url Helpers

    private static IReadOnlyCollection<(string, string)> GetExternalUrls(BaseItem item)
        => (item, item.GetProviderId(ProviderNames.Shoko)) switch {
            (_, string deflatedUrls) => [..InflateInfoUrls(deflatedUrls).Distinct()],

            (BoxSet boxSet, _) => [..GetCollectionUrls(boxSet)],
            (Person person, _) => [..GetPersonUrls(person)],

            _ => [],
        };

    private static IEnumerable<(string Name, string Url)> GetCollectionUrls(BoxSet boxSet) {
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

    private static IEnumerable<(string Name, string Url)> GetPersonUrls(Person person) {
        if (person.TryGetProviderId(ProviderNames.Anidb, out var creatorId)) {
            yield return ($"{ProviderNames.Anidb} (c{creatorId})", $"{ProviderUrls.Anidb}/creator/{creatorId}");
        }
    }

    #endregion

    #region Inflate / Deflate

    private static byte[] Deflate(byte[] data) {
        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, CompressionLevel.SmallestSize, true))
            ds.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    private static byte[] Inflate(byte[] compressed) {
        using var input = new MemoryStream(compressed);
        using var ds = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        ds.CopyTo(output);
        return output.ToArray();
    }

    private static string DeflateInfoUrls(IEnumerable<(string ProviderName, string Extras, string UrlPathname)> urls)
        => Convert.ToBase64String(Deflate(Encoding.UTF8.GetBytes(string.Join('\n', urls.Select(x => $"{x.ProviderName}|{x.Extras}|{x.UrlPathname}")))));

    private static IEnumerable<(string Name, string Url)> InflateInfoUrls(string deflatedUrls) {
        string? data;
        try {
            data = Encoding.UTF8.GetString(Inflate(Convert.FromBase64String(deflatedUrls)));
        }
        catch {
            yield break;
        }

        var config = Plugin.Instance.Configuration;
        var shokoUrl = config.WebUrl;
        var counters = new Dictionary<string, int>() {
            { ProviderNames.Shoko, 0 },
            { ProviderNames.Anidb, 0 },
            { ProviderNames.Tmdb, 0 },
            { ProviderNames.Tvdb, 0 },
        };
        foreach (var line in data.Split('\n')) {
            var (ns, extra, urlPathname) = line.Split('|');

            if (string.IsNullOrWhiteSpace(extra) || string.IsNullOrWhiteSpace(urlPathname))
                continue;

            var (baseUrl, shouldShow, count) = ns switch {
                ProviderNames.Shoko => (
                    shokoUrl,
                    config.ThirdPartyDisplayLinkList.Contains(DescriptionProvider.Shoko),
                    counters[ns]++
                ),
                ProviderNames.Anidb => (
                    ProviderUrls.Anidb,
                    config.ThirdPartyDisplayLinkList.Contains(DescriptionProvider.AniDB),
                    counters[ns]++
                ),
                ProviderNames.Tmdb => (
                    ProviderUrls.Tmdb,
                    config.ThirdPartyDisplayLinkList.Contains(DescriptionProvider.TMDB),
                    counters[ns]++
                    ),
                ProviderNames.Tvdb => (
                    ProviderUrls.Tvdb,
                    config.ThirdPartyDisplayLinkList.Contains(DescriptionProvider.TvDB),
                    counters[ns]++
                ),
                _ => (null, false, 0),
            };
            if (baseUrl is null || !shouldShow || (config.MaxLinksPerTypeToShow > 0 && count >= config.MaxLinksPerTypeToShow))
                continue;

            extra = config.AddExtraInfoToLinkName  && !string.IsNullOrEmpty(extra)
                ? $"{ns} {extra}"
                : ns;
            yield return (extra, baseUrl + urlPathname);
        }
    }

    #endregion

    #region Public Methods

    public static string GetShowInfoUrls(API.Info.ShowInfo showInfo) {
        var result = new List<(string ProviderName, string Extras, string UrlPathname)>();
        AddShowInfoUrls(ref result, showInfo);
        return DeflateInfoUrls(result);
    }

    public static string GetSeasonInfoUrls(API.Info.SeasonInfo seasonInfo) {
        var result = new List<(string ProviderName, string Extras, string UrlPathname)>();
        AddSeasonInfoUrls(ref result, seasonInfo);
        return DeflateInfoUrls(result);
    }

    public static string GetEpisodeInfoUrls(API.Info.EpisodeInfo episodeInfo) {
        var result = new List<(string ProviderName, string Extras, string UrlPathname)>();
        AddEpisodeInfoUrls(ref result, episodeInfo);
        return DeflateInfoUrls(result);
    }

    public static string GetFileInfoUrls(API.Info.FileInfo fileInfo) {
        var result = new List<(string ProviderName, string Extras, string UrlPathname)> {
            (
                ProviderNames.Shoko,
                $"(f{fileInfo.Id}) (s{fileInfo.SeriesId})",
                $"/collection/series/{fileInfo.SeriesId}/files?fileId={fileInfo.Id}"
            ),
        };
        foreach (var (episodeInfo, _, _) in fileInfo.EpisodeList)
            AddEpisodeInfoUrls(ref result, episodeInfo);
        return DeflateInfoUrls(result);
    }

    #endregion

    #region Add Urls Helpers

    private static void AddShowInfoUrls(ref List<(string ProviderName, string Extras, string UrlPathname)> result, API.Info.ShowInfo showInfo) {
        foreach (var shokoInfo in showInfo.ShokoSeries)
            result.Add((
                ProviderNames.Shoko,
                $"(s{shokoInfo.ShokoSeriesId}) (g{shokoInfo.ShokoGroupId})",
                $"/collection/series/{shokoInfo.ShokoSeriesId}"
            ));

        foreach (var anidbInfo in showInfo.AnidbAnime)
            result.Add((
                ProviderNames.Anidb,
                $"(a{anidbInfo.AnidbAnimeId})",
                $"/anime/{anidbInfo.AnidbAnimeId}"
            ));

        foreach (var tmdbInfo in showInfo.TmdbShows) {
            if (tmdbInfo.UsesAlternateOrdering)
                result.Add((
                    ProviderNames.Tmdb,
                    $"(tv{tmdbInfo.TmdbShowId} > g{tmdbInfo.TmdbAlternateOrderingId})",
                    $"/tv/{tmdbInfo.TmdbShowId}/episode_group/{tmdbInfo.TmdbAlternateOrderingId}"
                ));
            else
                result.Add((
                    ProviderNames.Tmdb,
                    $"(tv{tmdbInfo.TmdbShowId})",
                    $"/tv/{tmdbInfo.TmdbShowId}"
                ));
            if (!string.IsNullOrEmpty(tmdbInfo.TvdbShowId))
                result.Add((
                    ProviderNames.Tvdb,
                    $"(tv{tmdbInfo.TvdbShowId})",
                    $"/?tab=series&id={tmdbInfo.TvdbShowId}"
                ));
        }

        foreach (var tmdbInfo in showInfo.TmdbMovies) {
            result.Add((
                ProviderNames.Tmdb,
                $"(m{tmdbInfo.TmdbMovieId})",
                $"/movie/{tmdbInfo.TmdbMovieId}"
            ));
            if (!string.IsNullOrEmpty(tmdbInfo.TmdbMovieCollectionId))
                result.Add((
                    ProviderNames.Tmdb,
                    $"(c{tmdbInfo.TmdbMovieCollectionId})",
                    $"/collection/{tmdbInfo.TmdbMovieCollectionId}"
                ));
        }
    }

    private static void AddSeasonInfoUrls(ref List<(string ProviderName, string Extras, string UrlPathname)> result, API.Info.SeasonInfo seasonInfo) {
        foreach (var shokoInfo in seasonInfo.ShokoSeries)
            result.Add((
                ProviderNames.Shoko,
                $"(s{shokoInfo.ShokoSeriesId}) (g{shokoInfo.ShokoGroupId})",
                $"/collection/series/{shokoInfo.ShokoSeriesId}"
            ));

        foreach (var anidbInfo in seasonInfo.AnidbAnime)
            result.Add((
                ProviderNames.Anidb,
                $"(a{anidbInfo.AnidbAnimeId})",
                $"/anime/{anidbInfo.AnidbAnimeId}"
            ));

        foreach (var tmdbInfo in seasonInfo.TmdbSeasons) {
            if (tmdbInfo.UsesAlternateOrdering)
                result.Add((
                    ProviderNames.Tmdb,
                    $"(tv{tmdbInfo.TmdbShowId} > g{tmdbInfo.TmdbAlternateOrderingId} > S{tmdbInfo.SeasonNumber})",
                    $"/tv/{tmdbInfo.TmdbShowId}/episode_group/{tmdbInfo.TmdbAlternateOrderingId}/group/{tmdbInfo.TmdbSeasonId}"
                ));
            else
                result.Add((
                    ProviderNames.Tmdb,
                    $"(s{tmdbInfo.TmdbSeasonId}) (tv{tmdbInfo.TmdbShowId} > S{tmdbInfo.SeasonNumber})",
                    $"/tv/{tmdbInfo.TmdbShowId}/season/{tmdbInfo.SeasonNumber}"
                ));
        }

        foreach (var tmdbInfo in seasonInfo.TmdbMovies) {
            result.Add((
                ProviderNames.Tmdb,
                $"(m{tmdbInfo.TmdbMovieId})",
                $"/movie/{tmdbInfo.TmdbMovieId}"
            ));
            if (!string.IsNullOrEmpty(tmdbInfo.TmdbMovieCollectionId))
                result.Add((
                    ProviderNames.Tmdb,
                    $"(c{tmdbInfo.TmdbMovieCollectionId})",
                    $"/collection/{tmdbInfo.TmdbMovieCollectionId}"
                ));
        }
    }

    private static void AddEpisodeInfoUrls(ref List<(string ProviderName, string Extras, string UrlPathname)> result, API.Info.EpisodeInfo episodeInfo) {
        foreach (var shokoInfo in episodeInfo.ShokoEpisodes)
            result.Add((
                ProviderNames.Shoko,
                $"(e{shokoInfo.ShokoEpisodeId}) (s{shokoInfo.ShokoSeriesId})",
                $"/collection/series/{shokoInfo.ShokoSeriesId}/episodes?episodeId={shokoInfo.ShokoEpisodeId}"
            ));

        foreach (var anidbInfo in episodeInfo.AnidbEpisodes)
            result.Add((
                ProviderNames.Anidb,
                $"(e{anidbInfo.AnidbEpisodeId}) (a{anidbInfo.AnidbAnimeId} > {anidbInfo.GetEpisodeNumberText()})",
                $"/episode/{anidbInfo.AnidbEpisodeId}"
            ));

        foreach (var tmdbInfo in episodeInfo.TmdbEpisodes) {
            if (tmdbInfo.UsesAlternateOrdering)
                result.Add((
                    ProviderNames.Tmdb,
                    $"(e{tmdbInfo.TmdbEpisodeId}) (tv{tmdbInfo.TmdbShowId} > g{tmdbInfo.TmdbAlternateOrderingId} > S{tmdbInfo.SeasonNumber}E{tmdbInfo.EpisodeNumber})",
                    $"/tv/{tmdbInfo.TmdbShowId}/season/{tmdbInfo.OriginalSeasonNumber}/episode/{tmdbInfo.OriginalEpisodeNumber}"
                ));
            else
                result.Add((
                    ProviderNames.Tmdb,
                    $"(e{tmdbInfo.TmdbEpisodeId}) (tv{tmdbInfo.TmdbShowId} > S{tmdbInfo.SeasonNumber}E{tmdbInfo.EpisodeNumber})",
                    $"/tv/{tmdbInfo.TmdbShowId}/season/{tmdbInfo.SeasonNumber}/episode/{tmdbInfo.EpisodeNumber}"
                ));
            if (!string.IsNullOrEmpty(tmdbInfo.TvdbEpisodeId))
                result.Add((
                    ProviderNames.Tvdb,
                    $"(e{tmdbInfo.TvdbEpisodeId})",
                    $"/?tab=episode&id={tmdbInfo.TvdbEpisodeId}"
                ));
        }

        foreach (var tmdbInfo in episodeInfo.TmdbMovies) {
            result.Add((
                ProviderNames.Tmdb,
                $"(m{tmdbInfo.TmdbMovieId})",
                $"/movie/{tmdbInfo.TmdbMovieId}"
            ));
            if (!string.IsNullOrEmpty(tmdbInfo.TmdbMovieCollectionId))
                result.Add((
                    ProviderNames.Tmdb,
                    $"(c{tmdbInfo.TmdbMovieCollectionId})",
                    $"/collection/{tmdbInfo.TmdbMovieCollectionId}"
                ));
        }
    }

    #endregion
}
