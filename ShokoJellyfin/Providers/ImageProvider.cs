using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace ShokoJellyfin.Providers
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

            if (item is Episode && !Plugin.Instance.Configuration.UseShokoThumbnails) return list;

            var id = item.GetProviderId("Shoko");

            if (string.IsNullOrEmpty(id))
            {
                _logger.LogInformation($"Shoko Scanner... Images not found ({item.Name})");
                return list;
            }
            
            _logger.LogInformation($"Shoko Scanner... Getting images ({item.Name} - {id})");

            if (item is Episode)
            {
                var tvdbEpisodeInfo = (await API.ShokoAPI.GetEpisodeTvDb(id)).FirstOrDefault();
                var imageUrl = Helper.GetImageUrl(tvdbEpisodeInfo?.Thumbnail);
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    list.Add(new RemoteImageInfo
                    {
                        ProviderName = Name,
                        Type = ImageType.Primary,
                        Url = imageUrl
                    });
                }
            }

            if (item is Series)
            {
                var images = await API.ShokoAPI.GetSeriesImages(id);
                
                foreach (var image in images.Posters)
                {
                    var imageUrl = Helper.GetImageUrl(image);
                    if (!string.IsNullOrEmpty(imageUrl))
                    {
                        list.Add(new RemoteImageInfo
                        {
                            ProviderName = Name,
                            Type = ImageType.Primary,
                            Url = imageUrl
                        });
                    }
                }
                
                foreach (var image in images.Fanarts)
                {
                    var imageUrl = Helper.GetImageUrl(image);
                    if (!string.IsNullOrEmpty(imageUrl))
                    {
                        list.Add(new RemoteImageInfo
                        {
                            ProviderName = Name,
                            Type = ImageType.Backdrop,
                            Url = imageUrl
                        });
                    }
                }
                
                foreach (var image in images.Banners)
                {
                    var imageUrl = Helper.GetImageUrl(image);
                    if (!string.IsNullOrEmpty(imageUrl))
                    {
                        list.Add(new RemoteImageInfo
                        {
                            ProviderName = Name,
                            Type = ImageType.Banner,
                            Url = imageUrl
                        });
                    }
                }
            }

            return list;
        }

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Banner };
        }
        
        public bool Supports(BaseItem item)
        {
            return item is Series || item is Episode;
        }
        
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
        }
    }
}