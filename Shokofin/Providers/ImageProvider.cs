using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.ExternalIds;
using Shokofin.Utils;
using Shokofin.Web;

namespace Shokofin.Providers;

public class ImageProvider(IHttpClientFactory _httpClientFactory, ILogger<ImageProvider> _logger, ShokoApiManager _apiManager, ShokoIdLookup _lookup) : IRemoteImageProvider, IHasOrder {
    public string Name => Plugin.MetadataProviderName;

    public int Order => 0;

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken) {
        var displayMode = !Plugin.Instance.Configuration.Image.DebugMode && ImageHostUrl.CurrentItemId is { } currentItemId && currentItemId == item.Id;
        var list = new List<RemoteImageInfo>();
        var metadataLanguage = item.GetPreferredMetadataLanguage();
        var baseKind = item.GetBaseItemKind();
        var trackerId = Plugin.Instance.Tracker.Add($"Providing images for {baseKind} \"{item.Name}\". (Path=\"{item.Path}\")");
        try {
            switch (item) {
                case Episode episode: {
                    var (fileInfo, seasonInfo, _) = await _apiManager.GetFileInfoByPath(episode.Path).ConfigureAwait(false);
                    if (fileInfo is not { EpisodeList.Count: > 0 } || seasonInfo is null)
                        break;

                    var episodeInfo = fileInfo.EpisodeList[0].Episode;
                    var images = await ImageUtility.GetEpisodeImages(episodeInfo, seasonInfo, metadataLanguage, displayMode, cancellationToken).ConfigureAwait(false);
                    list.AddRange(images);

                    _logger.LogInformation("Getting {Count} images for episode {EpisodeName} (Episode={EpisodeId},Language={MetadataLanguage})", list.Count, episode.Name, episodeInfo.Id, metadataLanguage);
                    break;
                }
                case Series series: {
                    if (!_lookup.TryGetSeasonIdFor(series, out var seasonId) || await _apiManager.GetShowInfoBySeasonId(seasonId).ConfigureAwait(false) is not { } showInfo)
                        break;

                    var images = await ImageUtility.GetShowImages(showInfo, metadataLanguage, displayMode, cancellationToken).ConfigureAwait(false);
                    list.AddRange(images);

                    _logger.LogInformation("Getting {Count} images for series {SeriesName} (MainSeason={MainSeasonId},Language={MetadataLanguage})", list.Count, series.Name, seasonId, metadataLanguage);
                    break;
                }
                case Season season: {
                    if (!_lookup.TryGetSeasonIdFor(season, out var seasonId) || await _apiManager.GetSeasonInfo(seasonId).ConfigureAwait(false) is not { } seasonInfo)
                        break;

                    var images = await ImageUtility.GetSeasonImages(seasonInfo, metadataLanguage, displayMode, cancellationToken).ConfigureAwait(false);
                    list.AddRange(images);

                    _logger.LogInformation("Getting {Count} images for season {SeasonNumber} in {SeriesName} (Season={SeasonId},Language={MetadataLanguage})", list.Count, season.IndexNumber, season.SeriesName, seasonId, metadataLanguage);
                    break;
                }
                case Movie movie: {
                    var (fileInfo, seasonInfo, _) = await _apiManager.GetFileInfoByPath(movie.Path).ConfigureAwait(false);
                    if (fileInfo is not { EpisodeList.Count: > 0 } || seasonInfo is null)
                        break;

                    var episodeInfo = fileInfo.EpisodeList[0].Episode;
                    var images = await ImageUtility.GetMovieImages(episodeInfo, seasonInfo, metadataLanguage, displayMode, cancellationToken).ConfigureAwait(false);
                    list.AddRange(images);

                    _logger.LogInformation("Getting {Count} images for movie {MovieName} (Episode={EpisodeId},Language={MetadataLanguage})", list.Count, movie.Name, episodeInfo.Id, metadataLanguage);
                    break;
                }
                case BoxSet collection: {
                    string? collectionId = null;
                    if (collection.TryGetProviderId(ProviderNames.ShokoCollectionForSeries, out var seasonId)) {
                        if (await _apiManager.GetSeasonInfo(seasonId).ConfigureAwait(false) is not { } seasonInfo)
                            break;

                        var images = await ImageUtility.GetCollectionImages(seasonInfo, metadataLanguage, displayMode, cancellationToken).ConfigureAwait(false);
                        list.AddRange(images);
                    }
                    else if (collection.TryGetProviderId(ProviderNames.ShokoCollectionForGroup, out collectionId)) {
                        if (
                            await _apiManager.GetCollectionInfo(collectionId).ConfigureAwait(false) is not { } collectionInfo ||
                            string.IsNullOrEmpty(collectionInfo.MainSeasonId) ||
                            await _apiManager.GetShowInfoBySeasonId(collectionInfo.MainSeasonId).ConfigureAwait(false) is not { } showInfo
                        )
                            break;

                        var images = await ImageUtility.GetCollectionImages(showInfo, metadataLanguage, displayMode, cancellationToken).ConfigureAwait(false);
                        list.AddRange(images);
                    }

                    _logger.LogInformation("Getting {Count} images for collection {CollectionName} (Collection={CollectionId},Season={SeasonId},Language={MetadataLanguage})", list.Count, collection.Name, collectionId, collectionId is null ? seasonId : null, metadataLanguage);
                    break;
                }
            }
            return list;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Threw unexpectedly for {BaseKind} {Name}; {Message}", baseKind, item.Name, ex.Message);
            return list;
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        => [ImageType.Primary, ImageType.Backdrop, ImageType.Logo];

    public bool Supports(BaseItem item)
        => item is Series or Season or Episode or Movie or BoxSet;

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken) {
        var index = url.IndexOf("Shokofin/Host");
        if (index is -1)
            return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest);
        url = $"{Plugin.Instance.Configuration.Url}/api/v3{url[(index + 20)..]}";
        return await _httpClientFactory.CreateClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
    }
}

