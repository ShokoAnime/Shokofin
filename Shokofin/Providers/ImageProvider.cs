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

        private readonly ShokoAPIManager ApiManager;

        public ImageProvider(IHttpClientFactory httpClientFactory, ILogger<ImageProvider> logger, ShokoAPIManager apiManager)
        {
            HttpClientFactory = httpClientFactory;
            Logger = logger;
            ApiManager = apiManager;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            try {
                Shokofin.API.Info.EpisodeInfo episodeInfo = null;
                Shokofin.API.Info.SeriesInfo seriesInfo = null;

                var filterLibrary = Plugin.Instance.Configuration.FilterOnLibraryTypes ? Utils.Ordering.GroupFilterType.Others : Utils.Ordering.GroupFilterType.Default;
                switch (item) {
                    case Episode: {
                        if (item.ProviderIds.TryGetValue("Shoko Episode", out var episodeId) && !string.IsNullOrEmpty(episodeId)) {
                            episodeInfo = await ApiManager.GetEpisodeInfo(episodeId);
                            if (episodeInfo != null)
                                Logger.LogInformation("Getting images for episode {EpisodeName} (Episode={EpisodeId})", episodeInfo.Shoko.Name, episodeId);
                        }
                        break;
                    }
                    case Series: {
                        if (item.ProviderIds.TryGetValue("Shoko Group", out var groupId) && !string.IsNullOrEmpty(groupId)) {
                            var groupInfo = await ApiManager.GetGroupInfo(groupId, filterLibrary);
                            seriesInfo = groupInfo?.DefaultSeries;
                            if (seriesInfo != null)
                                Logger.LogInformation("Getting images for series {SeriesName} (Series={SeriesId},Group={GroupId})", groupInfo.Shoko.Name, seriesInfo.Id, groupId);
                        }
                        else if (item.ProviderIds.TryGetValue("Shoko Series", out var seriesId) && !string.IsNullOrEmpty(seriesId)) {
                            seriesInfo = await ApiManager.GetSeriesInfo(seriesId);
                            if (seriesInfo != null)
                                Logger.LogInformation("Getting images for series {SeriesName} (Series={SeriesId})", seriesInfo.Shoko.Name, seriesId);
                        }
                        break;
                    }
                    case Season season: {
                        if (season.IndexNumber.HasValue && season.Series.ProviderIds.TryGetValue("Shoko Series", out var seriesId) && !string.IsNullOrEmpty(seriesId)) {
                            var groupInfo = await ApiManager.GetGroupInfoForSeries(seriesId, filterLibrary);
                            seriesInfo = groupInfo?.GetSeriesInfoBySeasonNumber(season.IndexNumber.Value);
                            if (seriesInfo != null)
                                Logger.LogInformation("Getting images for season {SeasonNumber} in {SeriesName} (Series={SeriesId},Group={GroupId})", season.IndexNumber.Value, groupInfo.Shoko.Name, seriesInfo.Id, groupInfo.Id);
                        }
                        break;
                    }
                    case Movie:
                    case BoxSet: {
                        if (item.ProviderIds.TryGetValue("Shoko Series", out var seriesId) && !string.IsNullOrEmpty(seriesId)) {
                            seriesInfo = await ApiManager.GetSeriesInfo(seriesId);
                            if (seriesInfo != null)
                                Logger.LogInformation("Getting images for movie or box-set {MovieName} (Series={SeriesId})", seriesInfo.Shoko.Name, seriesId);
                        }
                        break;
                    }
                }

                if (episodeInfo != null) {
                    AddImage(ref list, ImageType.Primary, episodeInfo?.TvDB?.Thumbnail);
                }
                if (seriesInfo != null) {
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

                Logger.LogInformation("List got {Count} item(s). (Series={SeriesId},Episode={EpisodeId})", list.Count, seriesInfo?.Id ?? null, episodeInfo?.Id ?? null);
                return list;
            }
            catch (Exception e) {
                Logger.LogError(e, $"Threw unexpectedly; {e.Message}");
                return list;
            }
        }

        private void AddImage(ref List<RemoteImageInfo> list, ImageType imageType, API.Models.Image image)
        {
            var imageInfo = GetImage(image, imageType);
            if (imageInfo != null)
                list.Add(imageInfo);
        }

        private RemoteImageInfo GetImage(API.Models.Image image, ImageType imageType)
        {
            var imageUrl = image?.ToURLString();
            if (string.IsNullOrEmpty(imageUrl) || image.RelativeFilepath.Equals("/"))
                return null;
            return new RemoteImageInfo {
                ProviderName = "Shoko",
                Type = imageType,
                Url = imageUrl
            };
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
