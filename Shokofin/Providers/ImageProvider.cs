using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.ExternalIds;

namespace Shokofin.Providers;

public class ImageProvider : IRemoteImageProvider, IHasOrder
{
    public string Name => Plugin.MetadataProviderName;

    public int Order => 0;

    private readonly IHttpClientFactory HttpClientFactory;

    private readonly ILogger<ImageProvider> Logger;

    private readonly ShokoAPIClient ApiClient;

    private readonly ShokoAPIManager ApiManager;

    private readonly IIdLookup Lookup;

    public ImageProvider(IHttpClientFactory httpClientFactory, ILogger<ImageProvider> logger, ShokoAPIClient apiClient, ShokoAPIManager apiManager, IIdLookup lookup)
    {
        HttpClientFactory = httpClientFactory;
        Logger = logger;
        ApiClient = apiClient;
        ApiManager = apiManager;
        Lookup = lookup;
    }

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var list = new List<RemoteImageInfo>();
        try {
            switch (item) {
                case Episode episode: {
                    if (Lookup.TryGetEpisodeIdFor(episode, out var episodeId)) {
                        var episodeInfo = await ApiManager.GetEpisodeInfo(episodeId);
                        if (episodeInfo != null) {
                            AddImagesForEpisode(ref list, episodeInfo);
                        }
                        Logger.LogInformation("Getting {Count} images for episode {EpisodeName} (Episode={EpisodeId})", list.Count, episode.Name, episodeId);
                    }
                    break;
                }
                case Series series: {
                    if (Lookup.TryGetSeriesIdFor(series, out var seriesId)) {
                        var seriesImages = await ApiClient.GetSeriesImages(seriesId);
                        if (seriesImages != null) {
                            AddImagesForSeries(ref list, seriesImages);
                        }
                        // Also attach any images linked to the "seasons" (AKA series within the group).
                        list =  list
                            .Concat(
                                series.GetSeasons(null, new(true))
                                    .Cast<Season>()
                                    .SelectMany(season => GetImages(season, cancellationToken).Result)
                            )
                            .DistinctBy(image => image.Url)
                            .ToList();
                        Logger.LogInformation("Getting {Count} images for series {SeriesName} (Series={SeriesId})", list.Count, series.Name, seriesId);
                    }
                    break;
                }
                case Season season: {
                    if (Lookup.TryGetSeriesIdFor(season, out var seriesId)) {
                        var seriesImages = await ApiClient.GetSeriesImages(seriesId);
                        if (seriesImages != null) {
                            AddImagesForSeries(ref list, seriesImages);
                        }
                        Logger.LogInformation("Getting {Count} images for season {SeasonNumber} in {SeriesName} (Series={SeriesId})", list.Count, season.IndexNumber, season.SeriesName, seriesId);
                    }
                    break;
                }
                case Movie movie: {
                    if (Lookup.TryGetSeriesIdFor(movie, out var seriesId)) {
                        var seriesImages = await ApiClient.GetSeriesImages(seriesId);
                        if (seriesImages != null) {
                            AddImagesForSeries(ref list, seriesImages);
                        }
                        Logger.LogInformation("Getting {Count} images for movie {MovieName} (Series={SeriesId})", list.Count, movie.Name, seriesId);
                    }
                    break;
                }
                case BoxSet boxSet: {
                    if (!boxSet.ProviderIds.TryGetValue(ShokoCollectionSeriesId.Name, out var seriesId)) {
                        if (boxSet.ProviderIds.TryGetValue(ShokoCollectionGroupId.Name, out var collectionId))
                            seriesId = (await ApiManager.GetCollectionInfoForGroup(collectionId))?.Shoko.IDs.MainSeries.ToString();
                    }
                    if (!string.IsNullOrEmpty(seriesId)) {
                        var seriesImages = await ApiClient.GetSeriesImages(seriesId);
                        if (seriesImages != null) {
                            AddImagesForSeries(ref list, seriesImages);
                        }
                        Logger.LogInformation("Getting {Count} images for box-set {BoxSetName} (Series={SeriesId})", list.Count, boxSet.Name, seriesId);
                    }
                    break;
                }
            }
            return list;
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
            return list;
        }
    }

    private static void AddImagesForEpisode(ref List<RemoteImageInfo> list, API.Info.EpisodeInfo episodeInfo)
    {
        AddImage(ref list, ImageType.Primary, episodeInfo?.TvDB?.Thumbnail);
    }

    private static void AddImagesForSeries(ref List<RemoteImageInfo> list, API.Models.Images images)
    {
        foreach (var image in images.Posters.OrderByDescending(image => image.IsDefault))
            AddImage(ref list, ImageType.Primary, image);
        foreach (var image in images.Fanarts.OrderByDescending(image => image.IsDefault))
            AddImage(ref list, ImageType.Backdrop, image);
        foreach (var image in images.Banners.OrderByDescending(image => image.IsDefault))
            AddImage(ref list, ImageType.Banner, image);
    }

    private static void AddImage(ref List<RemoteImageInfo> list, ImageType imageType, API.Models.Image? image)
    {
        if (image == null || !image.IsAvailable)
            return;
        list.Add(new RemoteImageInfo {
            ProviderName = Plugin.MetadataProviderName,
            Type = imageType,
            Width = image.Width,
            Height = image.Height,
            Url = image.ToURLString(),
        });
    }

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        => new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Banner };

    public bool Supports(BaseItem item)
        => item is Series or Season or Episode or Movie or BoxSet;

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => HttpClientFactory.CreateClient().GetAsync(url, cancellationToken);
}
