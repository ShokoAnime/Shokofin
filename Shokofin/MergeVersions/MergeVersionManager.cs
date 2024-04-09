using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Shokofin.ExternalIds;

namespace Shokofin.MergeVersions;

/// <summary>
/// Responsible for merging multiple versions of the same video together into a
/// single UI element (by linking the videos together and letting Jellyfin
/// handle the rest).
/// </summary>
///
/// Based upon;
/// https://github.com/danieladov/jellyfin-plugin-mergeversions
public class MergeVersionsManager
{
    /// <summary>
    /// Library manager. Used to fetch items from the library.
    /// </summary>
    private readonly ILibraryManager LibraryManager;

    /// <summary>
    /// Shoko ID Lookup. Used to check if the plugin is enabled for the videos.
    /// </summary>
    private readonly IIdLookup Lookup;

    /// <summary>
    /// Used by the DI IoC to inject the needed interfaces.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="lookup">Shoko ID Lookup.</param>
    /// <param name="logger">Logger.</param>
    public MergeVersionsManager(ILibraryManager libraryManager, IIdLookup lookup)
    {
        LibraryManager = libraryManager;
        Lookup = lookup;
    }

    #region Shared

    /// <summary>
    /// Group and merge all videos with a Shoko Episode ID set.
    /// </summary>
    /// <param name="progress">Progress indicator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async task that will silently complete when the merging is
    /// complete.</returns>
    public async Task MergeAll(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Shared progress;
        double episodeProgressValue = 0d, movieProgressValue = 0d;

        // Setup the movie task.
        var movieProgress = new Progress<double>(value => {
            movieProgressValue = value / 2d;
            progress?.Report(movieProgressValue + episodeProgressValue);
        });
        var movieTask = MergeAllMovies(movieProgress, cancellationToken);

        // Setup the episode task.
        var episodeProgress = new Progress<double>(value => {
            episodeProgressValue = value / 2d;
            progress?.Report(movieProgressValue + episodeProgressValue);
        });
        var episodeTask = MergeAllEpisodes(episodeProgress, cancellationToken);

        // Run them in parallel.
        await Task.WhenAll(movieTask, episodeTask);
    }

    /// <summary>
    /// Split up all merged videos with a Shoko Episode ID set.
    /// </summary>
    /// <param name="progress">Progress indicator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async task that will silently complete when the splitting is
    /// complete.</returns>
    public async Task SplitAll(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Shared progress;
        double episodeProgressValue = 0d, movieProgressValue = 0d;

        // Setup the movie task.
        var movieProgress = new Progress<double>(value => {
            movieProgressValue = value / 2d;
            progress?.Report(movieProgressValue + episodeProgressValue);
        });
        var movieTask = SplitAllMovies(movieProgress, cancellationToken);

        // Setup the episode task.
        var episodeProgress = new Progress<double>(value => {
            episodeProgressValue = value / 2d;
            progress?.Report(movieProgressValue + episodeProgressValue);
            progress?.Report(50d + (value / 2d));
        });
        var episodeTask = SplitAllEpisodes(episodeProgress, cancellationToken);

        // Run them in parallel.
        await Task.WhenAll(movieTask, episodeTask);
    }

    #endregion Shared
    #region Movies

    /// <summary>
    /// Get all movies with a Shoko Episode ID set across all libraries.
    /// </summary>
    /// <returns>A list of all movies with a Shoko Episode ID set.</returns>
    private List<Movie> GetMoviesFromLibrary()
    {
        return LibraryManager.GetItemList(new() {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                IsVirtualItem = false,
                Recursive = true,
                HasAnyProviderId = new Dictionary<string, string> { {ShokoEpisodeId.Name, string.Empty } },
            })
            .Cast<Movie>()
            .Where(Lookup.IsEnabledForItem)
            .ToList();
    }

    /// <summary>
    /// Merge movie entries together.
    /// </summary>
    /// <param name="movies">Movies to merge.</param>
    /// <returns>An async task that will silently complete when the merging is
    /// complete.</returns>
    public static async Task MergeMovies(IEnumerable<Movie> movies)
        => await MergeVideos(movies.Cast<Video>().OrderBy(e => e.Id).ToList());

    /// <summary>
    /// Merge all movie entries with a Shoko Episode ID set.
    /// </summary>
    /// <param name="progress">Progress indicator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async task that will silently complete when the merging is
    /// complete.</returns>
    public async Task MergeAllMovies(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (Plugin.Instance.Configuration.EXPERIMENTAL_SplitThenMergeMovies) {
            await SplitAndMergeAllMovies(progress, cancellationToken);
            return;
        }

        // Merge all movies with more than one version.
        var movies = GetMoviesFromLibrary();
        var duplicationGroups = movies
            .GroupBy(x => (x.GetTopParent()?.Path, x.ProviderIds[ShokoEpisodeId.Name]))
            .Where(x => x.Count() > 1)
            .ToList();
        double currentCount = 0d;
        double totalGroups = duplicationGroups.Count;
        foreach (var movieGroup in duplicationGroups) {
            // Handle cancellation and update progress.
            cancellationToken.ThrowIfCancellationRequested();
            var percent = (currentCount++ / totalGroups) * 100;
            progress?.Report(percent);

            // Link the movies together as alternate sources.
            await MergeMovies(movieGroup);
        }

        progress?.Report(100);
    }

    /// <summary>
    /// Split up all existing merged movies with a Shoko Episode ID set.
    /// </summary>
    /// <param name="progress">Progress indicator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async task that will silently complete when the splitting is
    /// complete.</returns>
    public async Task SplitAllMovies(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Split up any existing merged movies.
        var movies = GetMoviesFromLibrary();
        double currentCount = 0d;
        double totalMovies = movies.Count;
        foreach (var movie in movies) {
            // Handle cancellation and update progress.
            cancellationToken.ThrowIfCancellationRequested();
            var percent = (currentCount++ / totalMovies) * 100d;
            progress?.Report(percent);

            // Remove all alternate sources linked to the movie.
            await RemoveAlternateSources(movie);
        }

        progress?.Report(100);
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="progress">Progress indicator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async task that will silently complete when the splitting
    /// followed by merging is complete.</returns>
    private async Task SplitAndMergeAllMovies(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Split up any existing merged movies.
        var movies = GetMoviesFromLibrary();
        double currentCount = 0d;
        double totalCount = movies.Count;
        foreach (var movie in movies) {
            // Handle cancellation and update progress.
            cancellationToken.ThrowIfCancellationRequested();
            var percent = (currentCount++ / totalCount) * 50d;
            progress?.Report(percent);

            // Remove all alternate sources linked to the movie.
            await RemoveAlternateSources(movie);
        }

        // Merge all movies with more than one version (again).
        var duplicationGroups = movies
            .GroupBy(movie => (movie.GetTopParent()?.Path, movie.ProviderIds[ShokoEpisodeId.Name]))
            .Where(movie => movie.Count() > 1)
            .ToList();
        currentCount = 0d;
        totalCount = duplicationGroups.Count;
        foreach (var movieGroup in duplicationGroups) {
            // Handle cancellation and update progress.
            cancellationToken.ThrowIfCancellationRequested();
            var percent = 50d + ((currentCount++ / totalCount) * 50d);
            progress?.Report(percent);

            // Link the movies together as alternate sources.
            await MergeMovies(movieGroup);
        }

        progress?.Report(100);
    }

    #endregion Movies
    #region Episodes

    /// <summary>
    /// Get all episodes with a Shoko Episode ID set across all libraries.
    /// </summary>
    /// <returns>A list of all episodes with a Shoko Episode ID set.</returns>
    private List<Episode> GetEpisodesFromLibrary()
    {
        return LibraryManager.GetItemList(new() {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                HasAnyProviderId = new Dictionary<string, string> { {ShokoEpisodeId.Name, string.Empty } },
                IsVirtualItem = false,
                Recursive = true,
            })
            .Cast<Episode>()
            .Where(Lookup.IsEnabledForItem)
            .ToList();
    }

    /// <summary>
    /// Merge episode entries together.
    /// </summary>
    /// <param name="episodes">Episodes to merge.</param>
    /// <returns>An async task that will silently complete when the merging is
    /// complete.</returns>
    public static async Task MergeEpisodes(IEnumerable<Episode> episodes)
        => await MergeVideos(episodes.Cast<Video>().OrderBy(e => e.Id).ToList());

    /// <summary>
    /// Split up all existing merged versions of each movie and merge them
    /// again afterwards. Only applied to movies with a Shoko Episode ID set.
    /// </summary>
    /// <param name="progress">Progress indicator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async task that will silently complete when the merging is
    /// complete.</returns>
    public async Task MergeAllEpisodes(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (Plugin.Instance.Configuration.EXPERIMENTAL_SplitThenMergeEpisodes) {
            await SplitAndMergeAllEpisodes(progress, cancellationToken);
            return;
        }

        // Merge episodes with more than one version, and with the same number
        // of additional episodes.
        var episodes = GetEpisodesFromLibrary();
        var duplicationGroups = episodes
            .GroupBy(e => (e.GetTopParent()?.Path, $"{e.ProviderIds[ShokoEpisodeId.Name]}-{(e.IndexNumberEnd ?? e.IndexNumber ?? 1) - (e.IndexNumber ?? 1)}"))
            .Where(e => e.Count() > 1)
            .ToList();
        double currentCount = 0d;
        double totalGroups = duplicationGroups.Count;
        foreach (var episodeGroup in duplicationGroups) {
            // Handle cancellation and update progress.
            cancellationToken.ThrowIfCancellationRequested();
            var percent = (currentCount++ / totalGroups) * 100d;
            progress?.Report(percent);

            // Link the episodes together as alternate sources.
            await MergeEpisodes(episodeGroup);
        }

        progress?.Report(100);
    }

    /// <summary>
    /// Split up all existing merged episodes with a Shoko Episode ID set.
    /// </summary>
    /// <param name="progress">Progress indicator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async task that will silently complete when the splitting is
    /// complete.</returns>
    public async Task SplitAllEpisodes(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Split up any existing merged episodes.
        var episodes = GetEpisodesFromLibrary();
        double currentCount = 0d;
        double totalEpisodes = episodes.Count;
        foreach (var e in episodes) {
            // Handle cancellation and update progress.
            cancellationToken.ThrowIfCancellationRequested();
            var percent = (currentCount++ / totalEpisodes) * 100d;
            progress?.Report(percent);

            // Remove all alternate sources linked to the episode.
            await RemoveAlternateSources(e);
        }

        progress?.Report(100);
    }

    /// <summary>
    /// Split up all existing merged versions of each episode and merge them
    /// again afterwards. Only applied to episodes with a Shoko Episode ID set.
    /// </summary>
    /// <param name="progress">Progress indicator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async task that will silently complete when the splitting
    /// followed by merging is complete.</returns>
    private async Task SplitAndMergeAllEpisodes(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Split up any existing merged episodes.
        var episodes = GetEpisodesFromLibrary();
        double currentCount = 0d;
        double totalCount = episodes.Count;
        foreach (var e in episodes) {
            // Handle cancellation and update progress.
            cancellationToken.ThrowIfCancellationRequested();
            var percent = (currentCount++ / totalCount) * 100d;
            progress?.Report(percent);

            // Remove all alternate sources linked to the episode.
            await RemoveAlternateSources(e);
        }

        // Merge episodes with more than one version (again), and with the same
        // number of additional episodes.
        var duplicationGroups = episodes
            .GroupBy(e => (e.GetTopParent()?.Path, $"{e.ProviderIds[ShokoEpisodeId.Name]}-{(e.IndexNumberEnd ?? e.IndexNumber ?? 1) - (e.IndexNumber ?? 1)}"))
            .Where(e => e.Count() > 1)
            .ToList();
        currentCount = 0d;
        totalCount = duplicationGroups.Count;
        foreach (var episodeGroup in duplicationGroups) {
            // Handle cancellation and update progress.
            cancellationToken.ThrowIfCancellationRequested();
            var percent = currentCount++ / totalCount * 100d;
            progress?.Report(percent);

            // Link the episodes together as alternate sources.
            await MergeEpisodes(episodeGroup);
        }
    }

    #endregion Episodes

    /// <summary>
    /// Merges multiple videos into a single UI element.
    /// </summary>
    ///
    /// Modified from;
    /// https://github.com/jellyfin/jellyfin/blob/9c97c533eff94d25463fb649c9572234da4af1ea/Jellyfin.Api/Controllers/VideosController.cs#L192
    private static async Task MergeVideos(List<Video> videos)
    {
        if (videos.Count < 2)
            return;

        var primaryVersion = videos.FirstOrDefault(i => i.MediaSourceCount > 1 && string.IsNullOrEmpty(i.PrimaryVersionId)) ??
            videos
                .OrderBy(i =>
                {
                    if (i.Video3DFormat.HasValue || i.VideoType != VideoType.VideoFile)
                        return 1;

                    return 0;
                })
                .ThenByDescending(i => i.GetDefaultVideoStream()?.Width ?? 0)
                .First();

        // Add any videos not already linked to the primary version to the list.
        var alternateVersionsOfPrimary = primaryVersion.LinkedAlternateVersions
            .ToList();
        foreach (var video in videos.Where(v => !v.Id.Equals(primaryVersion.Id)))
        {
            video.SetPrimaryVersionId(primaryVersion.Id.ToString("N", CultureInfo.InvariantCulture));
            if (!alternateVersionsOfPrimary.Any(i => string.Equals(i.Path, video.Path, StringComparison.OrdinalIgnoreCase))) {
                alternateVersionsOfPrimary.Add(new() {
                    Path = video.Path,
                    ItemId = video.Id,
                });
            }

            foreach (var linkedItem in video.LinkedAlternateVersions) {
                if (!alternateVersionsOfPrimary.Any(i => string.Equals(i.Path, linkedItem.Path, StringComparison.OrdinalIgnoreCase)))
                    alternateVersionsOfPrimary.Add(linkedItem);
            }

            // Reset the linked alternate versions for the linked videos.
            if (video.LinkedAlternateVersions.Length > 0)
                video.LinkedAlternateVersions = Array.Empty<LinkedChild>();

            // Save the changes back to the repository.
            await video.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None)
                .ConfigureAwait(false);
        }

        primaryVersion.LinkedAlternateVersions = alternateVersionsOfPrimary
            .ToArray();
        await primaryVersion.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Removes all alternate video sources from a video and all it's linked
    /// videos.
    /// </summary>
    /// <param name="baseItem">The primary video to clean up.</param>
    ///
    /// Modified from;
    /// https://github.com/jellyfin/jellyfin/blob/9c97c533eff94d25463fb649c9572234da4af1ea/Jellyfin.Api/Controllers/VideosController.cs#L152
    private async Task RemoveAlternateSources(Video video)
    {
        // Find the primary video.
        if (video.LinkedAlternateVersions.Length == 0) {
            // Ensure we're not running on an unlinked item.
            if (string.IsNullOrEmpty(video.PrimaryVersionId))
                return;

            // Make sure the primary video still exists before we proceed.
            if (LibraryManager.GetItemById(video.PrimaryVersionId) is not Video primaryVideo)
                return;
            video = primaryVideo;
        }

        // Remove the link for every linked video.
        foreach (var linkedVideo in video.GetLinkedAlternateVersions())
        {
            linkedVideo.SetPrimaryVersionId(null);
            linkedVideo.LinkedAlternateVersions = Array.Empty<LinkedChild>();
            await linkedVideo.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None)
                .ConfigureAwait(false);
        }

        // Remove the link for the primary video.
        video.SetPrimaryVersionId(null);
        video.LinkedAlternateVersions = Array.Empty<LinkedChild>();
        await video.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None)
            .ConfigureAwait(false);
    }
}
