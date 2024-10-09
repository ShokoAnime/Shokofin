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
        var metadataLanguage = item.GetPreferredMetadataLanguage();
        var baseKind = item.GetBaseItemKind();
        var sortPreferred = Plugin.Instance.Configuration.RespectPreferredImage;
        var trackerId = Plugin.Instance.Tracker.Add($"Providing images for {baseKind} \"{item.Name}\". (Path=\"{item.Path}\")");
        try {
            switch (item) {
                case Episode episode: {
                    if (Lookup.TryGetEpisodeIdFor(episode, out var episodeId)) {
                        var episodeImages = await ApiClient.GetEpisodeImages(episodeId);
                        if (episodeImages is not null)
                            AddImagesForEpisode(ref list, episodeImages, metadataLanguage, sortPreferred);
                        Logger.LogInformation("Getting {Count} images for episode {EpisodeName} (Episode={EpisodeId},Language={MetadataLanguage})", list.Count, episode.Name, episodeId, metadataLanguage);
                    }                    break;
                }
                case Series series: {
                    if (Lookup.TryGetSeriesIdFor(series, out var seriesId)) {
                        var seriesImages = await ApiClient.GetSeriesImages(seriesId);
                        if (seriesImages is not null) {
                            AddImagesForSeries(ref list, seriesImages, metadataLanguage, sortPreferred);
                            sortPreferred = false;
                        }
                        // Also attach any images linked to the "seasons" (AKA series within the group).
                        var showInfo = await ApiManager.GetShowInfoForSeries(seriesId);
                        if (showInfo is not null && !showInfo.IsStandalone) {
                            foreach (var seasonInfo in showInfo.SeasonList) {
                                seriesImages = await ApiClient.GetSeriesImages(seasonInfo.Id);
                                if (seriesImages is not null) {
                                    AddImagesForSeries(ref list, seriesImages, metadataLanguage, sortPreferred);
                                    sortPreferred = false;
                                }
                                if (seasonInfo?.ExtraIds is not null) {
                                    foreach (var extraId in seasonInfo.ExtraIds) {
                                        seriesImages = await ApiClient.GetSeriesImages(extraId);
                                        if (seriesImages is not null) {
                                            AddImagesForSeries(ref list, seriesImages, metadataLanguage, sortPreferred);
                                            sortPreferred = false;
                                        }
                                    }
                                }
                            }
                            list =  list
                                .DistinctBy(image => image.Url)
                                .ToList();
                        }
                        Logger.LogInformation("Getting {Count} images for series {SeriesName} (Series={SeriesId},Language={MetadataLanguage})", list.Count, series.Name, seriesId, metadataLanguage);
                    }
                    break;
                }
                case Season season: {
                    if (Lookup.TryGetSeriesIdFor(season, out var seriesId)) {
                        var seasonInfo = await ApiManager.GetSeasonInfoForSeries(seriesId);
                        var seriesImages = await ApiClient.GetSeriesImages(seriesId);
                        if (seriesImages is not null) {
                            AddImagesForSeries(ref list, seriesImages, metadataLanguage, sortPreferred);
                            sortPreferred = false;
                        }
                        if (seasonInfo?.ExtraIds is not null) {
                            foreach (var extraId in seasonInfo.ExtraIds) {
                                seriesImages = await ApiClient.GetSeriesImages(extraId);
                                if (seriesImages is not null) {
                                    AddImagesForSeries(ref list, seriesImages, metadataLanguage, sortPreferred);
                                    sortPreferred = false;
                                }
                            }
                            list =  list
                                .DistinctBy(image => image.Url)
                                .ToList();
                        }
                        Logger.LogInformation("Getting {Count} images for season {SeasonNumber} in {SeriesName} (Series={SeriesId},Language={MetadataLanguage})", list.Count, season.IndexNumber, season.SeriesName, seriesId, metadataLanguage);
                    }
                    break;
                }
                case Movie movie: {
                    if (Lookup.TryGetEpisodeIdFor(movie, out var episodeId)) {
                        var episodeImages = await ApiClient.GetEpisodeImages(episodeId);
                        if (episodeImages is not null)
                            AddImagesForSeries(ref list, episodeImages, metadataLanguage, sortPreferred);
                        Logger.LogInformation("Getting {Count} images for movie {MovieName} (Episode={EpisodeId},Language={MetadataLanguage})", list.Count, movie.Name, episodeId, metadataLanguage);
                    }
                    break;
                }
                case BoxSet collection: {
                    string? groupId = null;
                    if (!collection.TryGetProviderId(ShokoCollectionSeriesId.Name, out var seriesId) &&
                        collection.TryGetProviderId(ShokoCollectionGroupId.Name, out groupId))
                            seriesId = (await ApiManager.GetCollectionInfoForGroup(groupId))?.Shoko.IDs.MainSeries.ToString();
                    if (!string.IsNullOrEmpty(seriesId)) {
                        var seriesImages = await ApiClient.GetSeriesImages(seriesId);
                        if (seriesImages is not null)
                            AddImagesForSeries(ref list, seriesImages, metadataLanguage, sortPreferred);
                        Logger.LogInformation("Getting {Count} images for collection {CollectionName} (Group={GroupId},Series={SeriesId},Language={MetadataLanguage})", list.Count, collection.Name, groupId, groupId == null ? seriesId : null, metadataLanguage);
                    }
                    break;
                }
            }
            return list;
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Threw unexpectedly for {BaseKind} {Name}; {Message}", baseKind, item.Name, ex.Message);
            return list;
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    public static void AddImagesForEpisode(ref List<RemoteImageInfo> list, API.Models.EpisodeImages images, string metadataLanguage, bool sortList)
    {
        IEnumerable<API.Models.Image> imagesList = sortList
            ? images.Thumbnails.OrderByDescending(image => image.IsPreferred)
            : images.Thumbnails;
        foreach (var image in imagesList)
            AddImage(ref list, ImageType.Primary, image, metadataLanguage);
    }

    private static void AddImagesForSeries(ref List<RemoteImageInfo> list, API.Models.Images images, string metadataLanguage, bool sortList)
    {
        IEnumerable<API.Models.Image> imagesList = sortList
            ? images.Posters.OrderByDescending(image => image.IsPreferred)
            : images.Posters;
        foreach (var image in imagesList)
            AddImage(ref list, ImageType.Primary, image, sortList ? metadataLanguage : null);

        imagesList = sortList
            ? images.Backdrops.OrderByDescending(image => image.IsPreferred)
            : images.Backdrops;
        foreach (var image in imagesList)
            AddImage(ref list, ImageType.Backdrop, image, sortList ? metadataLanguage : null);

        imagesList = sortList
            ? images.Banners.OrderByDescending(image => image.IsPreferred)
            : images.Banners;
        foreach (var image in imagesList)
            AddImage(ref list, ImageType.Banner, image, sortList ? metadataLanguage : null);

        imagesList = sortList
            ? images.Logos.OrderByDescending(image => image.IsPreferred)
            : images.Logos;
        foreach (var image in imagesList)
            AddImage(ref list, ImageType.Logo, image, sortList ? metadataLanguage : null);
    }

    private static void AddImage(ref List<RemoteImageInfo> list, ImageType imageType, API.Models.Image? image, string? metadataLanguage)
    {
        if (image == null || !image.IsAvailable)
            return;

        list.Add(new RemoteImageInfo {
            ProviderName = Plugin.MetadataProviderName,
            Type = imageType,
            Width = image.Width,
            Height = image.Height,
            Url = image.ToURLString(),
            Language = Plugin.Instance.Configuration.AddImageLanguageCode
                ? !string.IsNullOrEmpty(metadataLanguage) && image.IsPreferred ? metadataLanguage : image.LanguageCode
                : null,
        });
    }

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        => [ImageType.Primary, ImageType.Backdrop, ImageType.Banner, ImageType.Logo];

    public bool Supports(BaseItem item)
        => item is Series or Season or Episode or Movie or BoxSet;

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var index = url.IndexOf("Plugin/Shokofin/Host");
        if (index is -1)
            return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest);
        url = $"{Plugin.Instance.Configuration.Url}/api/v3{url[(index + 20)..]}";
        return await HttpClientFactory.CreateClient().GetAsync(url, cancellationToken);
    }
}

