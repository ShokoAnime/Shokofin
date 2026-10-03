#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.ExternalIds;

namespace Shokofin.Providers;

/// <summary>
/// Provides similar series and movies from the suggestions in Shoko.
/// </summary>
public class SimilarItemsProvider(ILogger<SimilarItemsProvider> _logger, ShokoApiClient _apiClient, ShokoApiManager _apiManager, ShokoIdLookup _lookup) : IRemoteSimilarItemsProvider {
    public string Name => Plugin.MetadataProviderName;

    public MetadataPluginType Type => MetadataPluginType.SimilarityProvider;

    public TimeSpan? CacheDuration => TimeSpan.FromDays(1);

    // Hide the provider when the Shoko server doesn't have suggestions.
    public bool Supports(Type itemType)
        => _apiClient.HasSuggestions && (typeof(Series).IsAssignableFrom(itemType) || typeof(Movie).IsAssignableFrom(itemType));

    public async IAsyncEnumerable<SimilarItemReference> GetSimilarItemsAsync(BaseItem item, SimilarItemsQuery query, [EnumeratorCancellation] CancellationToken cancellationToken) {
        if (!_lookup.IsEnabledForItem(item))
            yield break;

        var seriesIds = await GetShokoSeriesIds(item);
        foreach (var seriesId in seriesIds) {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogTrace("Getting suggestions for series {SeriesId} for {ItemType} {ItemName} (Id={ItemId})", seriesId, item.GetType().Name, item.Name, item.Id);
            foreach (var suggestion in await _apiClient.GetSuggestionsForShokoSeries(seriesId)) {
                if (suggestion.SuggestedIDs.Shoko is not { } suggestedSeriesId)
                    continue;

                // Don't suggest the item itself, e.g. one of the other series
                // merged into the same show.
                var suggestedId = suggestedSeriesId.ToString();
                if (seriesIds.Contains(suggestedId))
                    continue;

                yield return new() {
                    ProviderName = ProviderNames.ShokoSeries,
                    ProviderId = suggestedId,
                    Score = suggestion.ApprovalRating is { } approvalRating ? (float)(approvalRating / 100) : null,
                };
            }
        }
    }

    private async Task<IReadOnlyList<string>> GetShokoSeriesIds(BaseItem item) {
        // Shows can be merged from multiple Shoko series, so ask for all of them.
        if (item is Series series && _lookup.TryGetSeasonIdFor(series, out var seasonId) && await _apiManager.GetShowInfoBySeasonId(seasonId) is { } showInfo) {
            var seriesIds = new List<string>();
            foreach (var shokoSeries in showInfo.ShokoSeries)
                seriesIds.Add(shokoSeries.ShokoSeriesId);
            return seriesIds;
        }

        if (item.TryGetProviderId(ProviderNames.ShokoSeries, out var seriesId))
            return [seriesId];

        return [];
    }
}
#endif
