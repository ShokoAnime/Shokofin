using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.ExternalIds;
using Shokofin.Utils;

namespace Shokofin.Providers;

public class BoxSetProvider : IRemoteMetadataProvider<BoxSet, BoxSetInfo>, IHasOrder
{
    public string Name => Plugin.MetadataProviderName;

    public int Order => 0;

    private readonly IHttpClientFactory HttpClientFactory;

    private readonly ILogger<BoxSetProvider> Logger;

    private readonly ShokoAPIManager ApiManager;

    public BoxSetProvider(IHttpClientFactory httpClientFactory, ILogger<BoxSetProvider> logger, ShokoAPIManager apiManager)
    {
        HttpClientFactory = httpClientFactory;
        Logger = logger;
        ApiManager = apiManager;
    }

    public async Task<MetadataResult<BoxSet>> GetMetadata(BoxSetInfo info, CancellationToken cancellationToken)
    {
        try {
            return Plugin.Instance.Configuration.CollectionGrouping switch
            {
                Ordering.CollectionCreationType.Shared => await GetShokoGroupedMetadata(info),
                _ => await GetDefaultMetadata(info),
            };
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
            return new MetadataResult<BoxSet>();
        }
    }

    public async Task<MetadataResult<BoxSet>> GetDefaultMetadata(BoxSetInfo info)
    {
        var result = new MetadataResult<BoxSet>();

        // First try to re-use any existing series id.
        if (!info.ProviderIds.TryGetValue(ShokoSeriesId.Name, out var seriesId))
            return result;

        var season = await ApiManager.GetSeasonInfoForSeries(seriesId);
        if (season == null) {
                Logger.LogWarning("Unable to find movie box-set info for name {Name} and path {Path}", info.Name, info.Path);
            return result;
        }

        if (season.EpisodeList.Count <= 1) {
            Logger.LogWarning("Series did not contain multiple movies! Skipping path {Path} (Series={SeriesId})", info.Path, season.Id);
            return result;
        }

        var (displayTitle, alternateTitle) = Text.GetSeasonTitles(season, info.MetadataLanguage);

        result.Item = new BoxSet {
            Name = displayTitle,
            OriginalTitle = alternateTitle,
            Overview = Text.GetDescription(season),
            PremiereDate = season.AniDB.AirDate,
            EndDate = season.AniDB.EndDate,
            ProductionYear = season.AniDB.AirDate?.Year,
            Tags = season.Tags.ToArray(),
            CommunityRating = season.AniDB.Rating.ToFloat(10),
        };
        result.Item.SetProviderId(ShokoSeriesId.Name, season.Id);
        if (Plugin.Instance.Configuration.AddAniDBId)
            result.Item.SetProviderId("AniDB", season.AniDB.Id.ToString());

        result.HasMetadata = true;

        return result;
    }

    private async Task<MetadataResult<BoxSet>> GetShokoGroupedMetadata(BoxSetInfo info)
    {
        // Filter out all manually created collections. We don't help those.
        var result = new MetadataResult<BoxSet>();
        if (!info.ProviderIds.TryGetValue(ShokoGroupId.Name, out var groupId))
            return result;

        var collection = await ApiManager.GetCollectionInfoForGroup(groupId);
        if (collection == null) {
            Logger.LogWarning("Unable to find collection info for name {Name} and path {Path}", info.Name, info.Path);
            return result;
        }

        result.Item = new BoxSet {
            Name = collection.Name,
            Overview = collection.Shoko.Description,
        };
        result.Item.SetProviderId(ShokoGroupId.Name, collection.Id);
        result.HasMetadata = true;

        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BoxSetInfo searchInfo, CancellationToken cancellationToken)
        => Task.FromResult<IEnumerable<RemoteSearchResult>>(new List<RemoteSearchResult>());


    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => HttpClientFactory.CreateClient().GetAsync(url, cancellationToken);
}
