using System;
using System.Collections.Generic;
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

namespace Shokofin.Providers
{
    public class ImageProvider : IRemoteImageProvider
    {
        public string Name => Plugin.MetadataProviderName;

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

                var filterLibrary = Plugin.Instance.Configuration.FilterOnLibraryTypes ? Utils.Ordering.GroupFilterType.Others : Utils.Ordering.GroupFilterType.Default;
                switch (item) {
                    case Episode episode: {
                        if (Lookup.TryGetEpisodeIdFor(episode, out var episodeId)) {
                            var episodeInfo = await ApiManager.GetEpisodeInfo(episodeId);
                            if (episodeInfo != null) {
                                AddImagesForEpisode(ref list, episodeInfo);
                                Logger.LogInformation("Getting {Count} images for episode {EpisodeName} (Episode={EpisodeId})", list.Count, episode.Name, episodeId);
                            }
                        }
                        break;
                    }
                    case Series series: {
                        if (Lookup.TryGetSeriesIdFor(series, out var seriesId)) {
                            if (Plugin.Instance.Configuration.SeriesGrouping == Utils.Ordering.GroupType.ShokoGroup) {
                                var groupInfo = await ApiManager.GetGroupInfoForSeries(seriesId, filterLibrary);
                                var seriesInfo = groupInfo?.DefaultSeries;
                                if (seriesInfo != null) {
                                    AddImagesForSeries(ref list, seriesInfo);
                                    Logger.LogInformation("Getting {Count} images for series {SeriesName} (Series={SeriesId},Group={GroupId})", list.Count, series.Name, seriesInfo.Id, groupInfo.Id);
                                }
                            }
                            else {
                                var seriesInfo = await ApiManager.GetSeriesInfo(seriesId);
                                if (seriesInfo != null) {
                                    AddImagesForSeries(ref list, seriesInfo);
                                    Logger.LogInformation("Getting {Count} images for series {SeriesName} (Series={SeriesId})", list.Count, series.Name, seriesId);
                                }
                            }
                        }
                        break;
                    }
                    case Season season: {
                        if (Lookup.TryGetSeriesIdFor(season, out var seriesId)) {
                            var seriesInfo = await ApiManager.GetSeriesInfo(seriesId);
                            if (seriesInfo != null) {
                                AddImagesForSeries(ref list, seriesInfo);
                                Logger.LogInformation("Getting {Count} images for season {SeasonNumber} in {SeriesName} (Series={SeriesId})", list.Count, season.IndexNumber, season.SeriesName, seriesInfo.Id);
                            }
                        }
                        break;
                    }
                    case Movie movie: {
                        if (Lookup.TryGetSeriesIdFor(movie, out var seriesId)) {
                            var seriesInfo = await ApiManager.GetSeriesInfo(seriesId);
                            if (seriesInfo != null) {
                                AddImagesForSeries(ref list, seriesInfo);
                                Logger.LogInformation("Getting {Count} images for movie {MovieName} (Series={SeriesId})", list.Count, movie.Name, seriesId);
                            }
                        }
                        break;
                    }
                    case BoxSet boxSet: {
                        if (Lookup.TryGetSeriesIdFor(boxSet, out var seriesId)) {
                            var seriesInfo = await ApiManager.GetSeriesInfo(seriesId);
                            if (seriesInfo != null) {
                                AddImagesForSeries(ref list, seriesInfo);
                                Logger.LogInformation("Getting {Count} images for box-set {BoxSetName} (Series={SeriesId})", list.Count, boxSet.Name, seriesId);
                            }
                        }
                        break;
                    }
                }
                return list;
            }
            catch (Exception e) {
                Logger.LogError(e, $"Threw unexpectedly; {e.Message}");
                return list;
            }
        }

        private void AddImagesForEpisode(ref List<RemoteImageInfo> list, API.Info.EpisodeInfo episodeInfo)
        {
            AddImage(ref list, ImageType.Primary, episodeInfo?.TvDB?.Thumbnail);
        }

        private void AddImagesForSeries(ref List<RemoteImageInfo> list, API.Info.SeriesInfo seriesInfo)
        {
            var images = seriesInfo.Shoko.Images;
            if (Plugin.Instance.Configuration.PreferAniDbPoster)
                AddImage(ref list, ImageType.Primary, seriesInfo.AniDB.Poster);
            foreach (var image in images?.Posters)
                AddImage(ref list, ImageType.Primary, image);
            if (!Plugin.Instance.Configuration.PreferAniDbPoster)
                AddImage(ref list, ImageType.Primary, seriesInfo.AniDB.Poster);
            foreach (var image in images?.Fanarts)
                AddImage(ref list, ImageType.Backdrop, image);
            foreach (var image in images?.Banners)
                AddImage(ref list, ImageType.Banner, image);
        }

        private void AddImage(ref List<RemoteImageInfo> list, ImageType imageType, API.Models.Image image)
        {
            if (image == null || !ApiClient.CheckImage(image.Path))
                return;
            list.Add(new RemoteImageInfo {
                ProviderName = Plugin.MetadataProviderName,
                Type = imageType,
                Url = image.ToURLString(),
            });
        }

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Banner };
        }

        public bool Supports(BaseItem item)
        {
            return item is Series || item is Season || item is Episode || item is Movie || item is BoxSet;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return HttpClientFactory.CreateClient().GetAsync(url, cancellationToken);
        }
    }
}
