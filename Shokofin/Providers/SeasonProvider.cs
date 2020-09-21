using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Shokofin.Providers
{
    public class SeasonProvider : IRemoteMetadataProvider<Season, SeasonInfo>
    {
        public string Name => "Shoko";
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<SeasonProvider> _logger;

        public SeasonProvider(IHttpClientFactory httpClientFactory, ILogger<SeasonProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Season>();

            var seasonName = GetSeasonName(info.Name);
            result.Item = new Season
            {
                Name = seasonName,
                SortName = seasonName,
                ForcedSortName = seasonName
            };
            result.HasMetadata = true;
            
            return result;
        }
        
        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo searchInfo, CancellationToken cancellationToken)
        {
            // Isn't called from anywhere. If it is called, I don't know from where.
            throw new NotImplementedException();
        }
        
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
        }

        private string GetSeasonName(string season)
        {
            switch (season)
            {
                case "Season 100":
                    return "Credits";
                case "Season 99":
                    return "Trailers";
                case "Season 98":
                    return "Misc.";
                default:
                    return season;
            }
        }
    }
}