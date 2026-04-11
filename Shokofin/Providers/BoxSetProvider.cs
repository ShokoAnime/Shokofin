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
using Shokofin.Extensions;
using Shokofin.ExternalIds;
using Shokofin.Utils;

namespace Shokofin.Providers;

public class BoxSetProvider(IHttpClientFactory _httpClientFactory, ILogger<BoxSetProvider> _logger, ShokoApiManager _apiManager)
    : IRemoteMetadataProvider<BoxSet, BoxSetInfo>, IHasOrder {
    public string Name => Plugin.MetadataProviderName;

    public int Order => -1;

    public async Task<MetadataResult<BoxSet>> GetMetadata(BoxSetInfo info, CancellationToken cancellationToken) {
        try {
            // Try to read the shoko group id
            if (info.Path.TryGetAttributeValue(ProviderNames.ShokoCollectionForGroup, out var collectionId))
                using (Plugin.Instance.Tracker.Enter($"Providing info for Collection \"{info.Name}\". (Path=\"{info.Path}\",Collection=\"{collectionId}\")"))
                    return await GetShokoGroupMetadata(info, collectionId).ConfigureAwait(false);

            // Try to read the shoko series id
            if (info.Path.TryGetAttributeValue(ProviderNames.ShokoCollectionForSeries, out var seasonId))
                using (Plugin.Instance.Tracker.Enter($"Providing info for Collection \"{info.Name}\". (Path=\"{info.Path}\",Season=\"{seasonId}\")"))
                    return await GetShokoSeriesMetadata(info, seasonId).ConfigureAwait(false);

            return new();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Threw unexpectedly while refreshing {Path}; {Message}", info.Path, ex.Message);
            return new MetadataResult<BoxSet>();
        }
    }

    private async Task<MetadataResult<BoxSet>> GetShokoSeriesMetadata(BoxSetInfo info, string seasonId) {
        // First try to re-use any existing series id.
        var result = new MetadataResult<BoxSet>();
        var seasonInfo = await _apiManager.GetSeasonInfo(seasonId).ConfigureAwait(false);
        if (seasonInfo == null) {
            _logger.LogWarning("Unable to find movie box-set info for name {Name} and path {Path}", info.Name, info.Path);
            return result;
        }

        var (displayTitle, alternateTitle) = TextUtility.GetCollectionTitles(seasonInfo, info.MetadataLanguage);

        _logger.LogInformation("Found collection {CollectionName} (Season={SeasonId},ExtraSeasons={ExtraIds})", displayTitle, seasonInfo.Id, seasonInfo.ExtraIds);

        result.Item = new BoxSet {
            Name = displayTitle,
            OriginalTitle = alternateTitle,
            Overview = TextUtility.GetCollectionDescription(seasonInfo, info.MetadataLanguage),
            PremiereDate = seasonInfo.PremiereDate,
            EndDate = seasonInfo.EndDate,
            ProductionYear = seasonInfo.PremiereDate?.Year,
            Tags = seasonInfo.Tags.ToArray(),
            CommunityRating = seasonInfo.CommunityRating.ToFloat(10),
        };
        result.Item.SetProviderId(ProviderNames.ShokoCollectionForSeries, seasonInfo.Id);
        result.HasMetadata = true;

        return result;
    }

    private async Task<MetadataResult<BoxSet>> GetShokoGroupMetadata(BoxSetInfo info, string collectionId) {
        // Filter out all manually created collections. We don't help those.
        var result = new MetadataResult<BoxSet>();
        var collectionInfo = await _apiManager.GetCollectionInfo(collectionId).ConfigureAwait(false);
        if (collectionInfo == null) {
            _logger.LogWarning("Unable to find collection info for name {Name} and path {Path}", info.Name, info.Path);
            return result;
        }

        var (displayTitle, alternateTitle) = TextUtility.GetCollectionTitles(collectionInfo, info.MetadataLanguage);
        displayTitle ??= collectionInfo.Title;

        _logger.LogInformation("Found collection {CollectionName} (Collection={CollectionId})", displayTitle, collectionInfo.Id);

        result.Item = new BoxSet {
            Name = displayTitle,
            OriginalTitle = alternateTitle,
            Overview = TextUtility.GetCollectionDescription(collectionInfo, info.MetadataLanguage),
        };
        result.Item.SetProviderId(ProviderNames.ShokoCollectionForGroup, collectionInfo.Id);
        result.HasMetadata = true;

        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BoxSetInfo searchInfo, CancellationToken cancellationToken)
        => Task.FromResult<IEnumerable<RemoteSearchResult>>([]);

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => await _httpClientFactory.CreateClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
}
