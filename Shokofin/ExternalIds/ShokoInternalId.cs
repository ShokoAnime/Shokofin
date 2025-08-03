using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Shokofin.API;
using Shokofin.Extensions;
using Shokofin.Utils;

namespace Shokofin.ExternalIds;

public class ShokoInternalId(ShokoIdLookup lookup, UsageTracker tracker) : IExternalId, IExternalUrlProvider {
    public static string Name => MetadataProvider.Custom.ToString();

    public const string SeriesNamespace = "shoko://series/";

    public const string EpisodeNamespace = "shoko://episode/";

    public const string FileNamespace = "shoko://file/";

    private readonly Queue<string> _nextNames = new();

    private readonly object _lock = new();

    #region IExternalId Implementation

    string IExternalId.ProviderName => Name;

    string IExternalId.Key => Name;

    ExternalIdMediaType? IExternalId.Type => null;

    string? IExternalId.UrlFormatString => null;

    bool IExternalId.Supports(IHasProviderIds item) => item is BoxSet or Series or Season or Video;

    #endregion

    #region IExternalUrlProvider Implementation

    string IExternalUrlProvider.Name => _nextNames.TryDequeue(out var name) ? name : Name;

    /// <inheritdoc/>
    IEnumerable<string> IExternalUrlProvider.GetExternalUrls(BaseItem item)
    {
        if (!lookup.IsEnabledForItem(item))
            yield break;

        lock (_lock) {
            _nextNames.Clear();
            var url = Plugin.Instance.Configuration.WebUrl;
            switch (item) {
                case BoxSet boxSet:
                    if (item.TryGetProviderId(ProviderNames.ShokoCollectionForGroup, out var collectionId) || item.Path.TryGetAttributeValue(ProviderNames.ShokoCollectionForGroup, out collectionId)) {
                        _nextNames.Enqueue(ProviderNames.ShokoGroup);
                        yield return $"{url}/collection/group/{collectionId}";
                    }
                    if (item.TryGetProviderId(ProviderNames.ShokoCollectionForSeries, out var seasonId) || item.Path.TryGetAttributeValue(ProviderNames.ShokoCollectionForSeries, out seasonId)) {
                        switch (seasonId[0]) {
                            case IdPrefix.TmdbMovie:
                            case IdPrefix.TmdbMovieCollection:
                            case IdPrefix.TmdbShow:
                                break;
                            default:
                                _nextNames.Enqueue(ProviderNames.ShokoSeries);
                                yield return $"{url}/collection/series/{seasonId}";
                                break;
                        }
                    }
                    break;

                case Series or Season:
                    if (item.TryGetProviderId(ProviderNames.ShokoGroup, out var groupId)) {
                        _nextNames.Enqueue(ProviderNames.ShokoGroup);
                        yield return $"{url}/collection/group/{groupId}";
                    }
                    if (item.TryGetProviderId(ProviderNames.ShokoSeries, out var seriesId)) {
                        _nextNames.Enqueue(ProviderNames.ShokoSeries);
                        yield return $"{url}/collection/series/{seriesId}";
                    }
                    break;

                case Video:
                    using (tracker.Enter("Get External Urls for Video")) {
                        if (lookup.TryGetFileAndSeriesIdFor(item, out var fileId, out seriesId)) {
                            _nextNames.Enqueue(ProviderNames.ShokoSeries);
                            yield return $"{url}/collection/series/{seriesId}";
                            _nextNames.Enqueue(ProviderNames.ShokoFile);
                            yield return $"{url}/collection/series/{seriesId}/files?fileId={fileId}";
                        }
                        if (lookup.TryGetEpisodeIdsFor(item, out var episodeIds)) {
                            foreach (var episodeId in episodeIds) {
                                switch (episodeId[0]) {
                                    case IdPrefix.TmdbMovie:
                                    case IdPrefix.TmdbShow:
                                        break;
                                    default:
                                        _nextNames.Enqueue(ProviderNames.ShokoEpisode);
                                        yield return $"{url}/collection/series/{seriesId}/episodes?episodeId={episodeId}";
                                        break;
                                }
                            }
                        }
                    }
                    break;
            }
        }
    }

    #endregion
}
