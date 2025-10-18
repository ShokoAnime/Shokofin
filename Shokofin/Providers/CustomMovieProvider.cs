using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Extensions;
using Shokofin.ExternalIds;
using Shokofin.MergeVersions;
using Shokofin.Resolvers;

namespace Shokofin.Providers;
#pragma warning disable IDE0059
#pragma warning disable IDE0290

/// <summary>
/// The custom movie provider. Responsible for de-duplicating physical movies.
/// </summary>
/// <remarks>
/// This needs to be it's own class because of internal Jellyfin shenanigans
/// about how a provider cannot also be a custom provider otherwise it won't
/// save the metadata.
/// </remarks>
public class CustomMovieProvider(ILogger<CustomMovieProvider> _logger, VirtualFileSystemService _vfsService, ILibraryManager _libraryManager, ShokoIdLookup _lookup, MergeVersionsManager _mergeVersionsManager) : IHasItemChangeMonitor, ICustomMetadataProvider<Movie> {
    public string Name => Plugin.MetadataProviderName;

    public bool HasChanged(BaseItem item, IDirectoryService directoryService) {
        // We're only interested in movies.
        if (item is not Movie movie)
            return false;

        // Abort if we're unable to get the shoko episode id.
        if (!movie.TryGetProviderId(ProviderNames.ShokoEpisode, out var episodeId))
            return false;

        return true;
    }

    public async Task<ItemUpdateType> FetchAsync(Movie movie, MetadataRefreshOptions options, CancellationToken cancellationToken) {
        if (!_lookup.IsEnabledForItem(movie) || !movie.TryGetSeasonId(out var seasonId) || !movie.TryGetEpisodeId(out var episodeId) || !movie.TryGetFileAndSeriesId(out var fileId, out var seriesId))
            return ItemUpdateType.None;

        var trackerId = Plugin.Instance.Tracker.Add($"Providing custom info for Movie \"{movie.Name}\". (Path=\"{movie.Path}\")");
        try {
            if (_vfsService.TryGetCurrentLibraryGenerationMode(movie.Path, out var iterativeGeneration, out var wasGenerated) && iterativeGeneration && !wasGenerated) {
                _logger.LogTrace("Skipped movie during iterative generation. (Season={SeasonId},Episode={EpisodeId})", seasonId, episodeId);
                return ItemUpdateType.None;
            }

            if (Plugin.Instance.Configuration.AutoMergeVersions && !_libraryManager.IsScanRunning && options.MetadataRefreshMode != MetadataRefreshMode.ValidationOnly) {
                await _mergeVersionsManager.SplitAndMergeMoviesByEpisodeId(episodeId).ConfigureAwait(false);
            }
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }

        return ItemUpdateType.None;
    }
}