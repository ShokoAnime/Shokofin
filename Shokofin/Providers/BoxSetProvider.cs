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

    public int Order => -1;

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
            // Try to read the shoko group id
            if (info.ProviderIds.TryGetValue(ShokoCollectionGroupId.Name, out var collectionId) ||
               info.Path.TryGetAttributeValue(ShokoCollectionGroupId.Name, out collectionId))
                return await GetShokoGroupMetadata(info, collectionId);

            // Try to read the shoko series id
            if (info.ProviderIds.TryGetValue(ShokoCollectionSeriesId.Name, out var seriesId) ||
                    info.Path.TryGetAttributeValue(ShokoCollectionSeriesId.Name, out seriesId))
                return await GetShokoSeriesMetadata(info, seriesId);

            return new();
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
            return new MetadataResult<BoxSet>();
        }
    }

    private async Task<MetadataResult<BoxSet>> GetShokoSeriesMetadata(BoxSetInfo info, string seriesId)
    {
        // First try to re-use any existing series id.
        var result = new MetadataResult<BoxSet>();
        var season = await ApiManager.GetSeasonInfoForSeries(seriesId);
        if (season == null) {
            Logger.LogWarning("Unable to find movie box-set info for name {Name} and path {Path}", info.Name, info.Path);
            return result;
        }

        var (displayTitle, alternateTitle) = Text.GetSeasonTitles(season, info.MetadataLanguage);

        Logger.LogInformation("Found collection {CollectionName} (Series={SeriesId})", displayTitle, season.Id);

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
        result.Item.SetProviderId(ShokoCollectionSeriesId.Name, season.Id);
        result.HasMetadata = true;

        return result;
    }

    private async Task<MetadataResult<BoxSet>> GetShokoGroupMetadata(BoxSetInfo info, string groupId)
    {
        // Filter out all manually created collections. We don't help those.
        var result = new MetadataResult<BoxSet>();
        var collection = await ApiManager.GetCollectionInfoForGroup(groupId);
        if (collection == null) {
            Logger.LogWarning("Unable to find collection info for name {Name} and path {Path}", info.Name, info.Path);
            return result;
        }

        Logger.LogInformation("Found collection {CollectionName} (Series={SeriesId})", collection.Name, collection.Id);

        result.Item = new BoxSet {
            Name = collection.Name,
            Overview = collection.Shoko.Description,
        };
        result.Item.SetProviderId(ShokoCollectionGroupId.Name, collection.Id);
        result.HasMetadata = true;

        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BoxSetInfo searchInfo, CancellationToken cancellationToken)
        => Task.FromResult<IEnumerable<RemoteSearchResult>>(new List<RemoteSearchResult>());


    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => HttpClientFactory.CreateClient().GetAsync(url, cancellationToken);
}
