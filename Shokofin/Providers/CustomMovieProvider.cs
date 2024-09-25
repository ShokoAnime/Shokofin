using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Shokofin.ExternalIds;
using Shokofin.MergeVersions;

namespace Shokofin.Providers;

/// <summary>
/// The custom movie provider. Responsible for de-duplicating physical movies.
/// </summary>
/// <remarks>
/// This needs to be it's own class because of internal Jellyfin shenanigans
/// about how a provider cannot also be a custom provider otherwise it won't
/// save the metadata.
/// </remarks>
public class CustomMovieProvider : ICustomMetadataProvider<Movie>
{
    public string Name => Plugin.MetadataProviderName;

    private readonly ILogger<CustomEpisodeProvider> _logger;

    private readonly ILibraryManager _libraryManager;

    private readonly MergeVersionsManager _mergeVersionsManager;

    public CustomMovieProvider(ILogger<CustomEpisodeProvider> logger, ILibraryManager libraryManager, MergeVersionsManager mergeVersionsManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _mergeVersionsManager = mergeVersionsManager;
    }

    public async Task<ItemUpdateType> FetchAsync(Movie movie, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        var itemUpdated = ItemUpdateType.None;
        if (movie.TryGetProviderId(ShokoEpisodeId.Name, out var episodeId) && Plugin.Instance.Configuration.AutoMergeVersions && !_libraryManager.IsScanRunning && options.MetadataRefreshMode != MetadataRefreshMode.ValidationOnly) {
            await _mergeVersionsManager.SplitAndMergeMoviesByEpisodeId(episodeId);
            itemUpdated |= ItemUpdateType.MetadataEdit;
        }

        return itemUpdated;
    }
}