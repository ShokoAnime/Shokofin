using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.Utils;

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
            try
            {
                switch (Plugin.Instance.Configuration.SeriesGrouping)
                {
                    default:
                        return GetDefaultMetadata(info, cancellationToken);
                    case OrderingUtil.SeriesGroupType.ShokoGroup:
                        return await GetShokoGroupedMetadata(info, cancellationToken);
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"{e.Message}\n{e.StackTrace}");
                return new MetadataResult<Season>();
            }
        }

        private MetadataResult<Season> GetDefaultMetadata(SeasonInfo info, CancellationToken cancellationToken)
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
        
        private async Task<MetadataResult<Season>> GetShokoGroupedMetadata(SeasonInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Season>();

            if (!info.SeriesProviderIds.ContainsKey("Shoko Group"))
            {
                _logger.LogWarning($"Shoko Scanner... Shoko Group id not stored for series");
                return result;
            }

            var groupId = info.SeriesProviderIds["Shoko Group"];
            var seasonNumber = info.IndexNumber ?? 1;
            var series = await DataUtil.GetSeriesInfoFromGroup(groupId, seasonNumber);
            if (series == null)
            {
                _logger.LogWarning($"Shoko Scanner... Unable to find series info for G{groupId}:S{seasonNumber}");
                return result;
            }
            _logger.LogInformation($"Shoko Scanner... Found series info for G{groupId}:S{seasonNumber}");

            var tags = await DataUtil.GetTags(series.ID);
            var ( displayTitle, alternateTitle ) = TextUtil.GetSeriesTitles(series.AniDB.Titles, series.Shoko.Name, info.MetadataLanguage);

            result.Item = new Season
            {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                IndexNumber = seasonNumber,
                Overview = TextUtil.SummarySanitizer(series.AniDB.Description),
                PremiereDate = series.AniDB.AirDate,
                EndDate = series.AniDB.EndDate,
                ProductionYear = series.AniDB.AirDate?.Year,
                Tags = tags,
                CommunityRating = DataUtil.GetRating(series.AniDB.Rating),
            };

            result.HasMetadata = true;

            result.ResetPeople();
            foreach (var person in await DataUtil.GetPeople(series.ID))
                result.AddPerson(person);

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