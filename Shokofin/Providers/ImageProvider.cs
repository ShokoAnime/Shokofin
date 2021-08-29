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
        public string Name => "Shoko";

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
                Shokofin.API.Info.EpisodeInfo episode = null;
                Shokofin.API.Info.SeriesInfo series = null;
                if (item is Episode) {
                    episode = await ApiManager.GetEpisodeInfo(item.GetProviderId("Shoko Episode"));
                }
                else if (item is Series) {
                    var groupId = item.GetProviderId("Shoko Group");
                    if (string.IsNullOrEmpty(groupId))
                        series = await ApiManager.GetSeriesInfo(item.GetProviderId("Shoko Series"));
                    else
                        series = (await ApiManager.GetGroupInfo(groupId))?.DefaultSeries;
                }
                else if (item is BoxSet || item is Movie) {
                    series = await ApiManager.GetSeriesInfo(item.GetProviderId("Shoko Series"));
                }
                else if (item is Season) {
                    series = await ApiManager.GetSeriesInfoFromGroup(item.GetParent()?.GetProviderId("Shoko Group"), item.IndexNumber ?? 1);
                }
                if (episode != null) {
                    Logger.LogInformation($"Getting episode images ({episode.Id} - {item.Name})");
                    AddImage(ref list, ImageType.Primary, episode?.TvDB?.Thumbnail);
                }
                if (series != null) {
                    Logger.LogInformation($"Getting series images ({series.Id} - {item.Name})");
                    var images = series.Shoko.Images;
                    AddImage(ref list, ImageType.Primary, series.AniDB.Poster);
                    foreach (var image in images?.Posters)
                        AddImage(ref list, ImageType.Primary, image);
                    foreach (var image in images?.Fanarts)
                        AddImage(ref list, ImageType.Backdrop, image);
                    foreach (var image in images?.Banners)
                        AddImage(ref list, ImageType.Banner, image);
                }

                Logger.LogInformation($"List got {list.Count} item(s).");
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
