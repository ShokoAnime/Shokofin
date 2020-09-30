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

namespace Shokofin.Providers
{
    public class ImageProvider : IRemoteImageProvider
    {
        public string Name => "Shoko";
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ImageProvider> _logger;
        
        public ImageProvider(IHttpClientFactory httpClientFactory, ILogger<ImageProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            try
            {
                string episodeId = null;
                string seriesId = null;
                if (item is Episode)
                {
                    episodeId = item.GetProviderId("Shoko Episode");
                }
                else if (item is Series || item is BoxSet || item is Movie)
                {
                    seriesId = item.GetProviderId("Shoko Series");
                }
                else if (item is Season)
                {
                    seriesId = item.GetParent()?.GetProviderId("Shoko Series");
                }
                if (episodeId != null)
                {
                    _logger.LogInformation($"Getting episode images ({episodeId} - {item.Name})");

                    var tvdbEpisodeInfo = (await API.ShokoAPI.GetEpisodeTvDb(episodeId)).FirstOrDefault();
                    AddImage(ref list, ImageType.Primary, tvdbEpisodeInfo?.Thumbnail);
                }
                if (seriesId != null)
                {
                    _logger.LogInformation($"Getting series images ({seriesId} - {item.Name})");
                    var images = await API.ShokoAPI.GetSeriesImages(seriesId);
                    foreach (var image in images?.Posters)
                        AddImage(ref list, ImageType.Primary, image);
                    foreach (var image in images?.Fanarts)
                        AddImage(ref list, ImageType.Backdrop, image);
                    foreach (var image in images?.Banners)
                        AddImage(ref list, ImageType.Banner, image);
                }

                _logger.LogInformation($"List got {list.Count} item(s).");
                return list;
            }
            catch (Exception e)
            {
                _logger.LogError(e.StackTrace);
                return list;
            }
        }

        private void AddImage(ref List<RemoteImageInfo> list, ImageType imageType, API.Models.Image image)
        {
            var imageUrl = Helper.GetImageUrl(image);
            if (!string.IsNullOrEmpty(imageUrl))
            {
                list.Add(new RemoteImageInfo
                {
                    ProviderName = Name,
                    Type = imageType,
                    Url = imageUrl
                });
            }
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
            return _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
        }
    }
}