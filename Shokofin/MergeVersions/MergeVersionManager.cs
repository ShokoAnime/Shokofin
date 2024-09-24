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
    public MergeVersionsManager(ILibraryManager libraryManager, IIdLookup lookup)
    {
        LibraryManager = libraryManager;
        Lookup = lookup;
    }

    #region Top Level

    /// <summary>
    /// Group and merge all videos with a Shoko Episode ID set.
    /// </summary>
    /// <param name="progress">Progress indicator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async task that will silently complete when the merging is
    /// complete.</returns>
    public async Task SplitAndMergeAll(IProgress<double>? progress, CancellationToken? cancellationToken = null)
    {
        // Shared progress;
        double episodeProgressValue = 0d, movieProgressValue = 0d;

        // Setup the movie task.
        var movieProgress = new Progress<double>(value => {
            movieProgressValue = value / 2d;
            progress?.Report(movieProgressValue + episodeProgressValue);
        });
        var movieTask = SplitAndMergeVideos(GetMoviesFromLibrary(), movieProgress, cancellationToken);

        // Setup the episode task.
        var episodeProgress = new Progress<double>(value => {
            episodeProgressValue = value / 2d;
            progress?.Report(movieProgressValue + episodeProgressValue);
        });
        var episodeTask = SplitAndMergeVideos(GetEpisodesFromLibrary(), episodeProgress, cancellationToken);

        // Run them in parallel.
        await Task.WhenAll(movieTask, episodeTask);

        progress?.Report(100d);
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
        var movieTask = SplitVideos(GetMoviesFromLibrary(), movieProgress, cancellationToken);

        // Setup the episode task.
        var episodeProgress = new Progress<double>(value => {
            episodeProgressValue = value / 2d;
            progress?.Report(movieProgressValue + episodeProgressValue);
            progress?.Report(50d + (value / 2d));
        });
        var episodeTask = SplitVideos(GetMoviesFromLibrary(), episodeProgress, cancellationToken);

        // Run them in parallel.
        await Task.WhenAll(movieTask, episodeTask);
    }

    #endregion

    #region Episode Level

    public async Task SplitAndMergeAllEpisodes(IProgress<double>? progress, CancellationToken? cancellationToken)
        => await SplitAndMergeVideos(GetEpisodesFromLibrary(), progress, cancellationToken);

    public async Task SplitAllEpisodes(IProgress<double>? progress, CancellationToken? cancellationToken)
        => await SplitVideos(GetEpisodesFromLibrary(), progress, cancellationToken);

    #endregion

    #region Movie Level

    public async Task SplitAndMergeAllMovies(IProgress<double>? progress, CancellationToken? cancellationToken)
        => await SplitAndMergeVideos(GetMoviesFromLibrary(), progress, cancellationToken);

    public async Task SplitAllMovies(IProgress<double>? progress, CancellationToken? cancellationToken)
        => await SplitVideos(GetMoviesFromLibrary(), progress, cancellationToken);

    #endregion

    #region Shared Methods

    /// <summary>
    /// Get all movies with a Shoko Episode ID set across all libraries.
    /// </summary>
    /// <param name="episodeId">Optional. The episode id if we want to filter to only movies with a given Shoko Episode ID.</param>
    /// <returns>A list of all movies with the given <paramref name="episodeId"/> set.</returns>
    public IReadOnlyList<Movie> GetMoviesFromLibrary(string episodeId = "")
        => LibraryManager
            .GetItemList(new() {
                IncludeItemTypes = [BaseItemKind.Movie],
                IsVirtualItem = false,
                Recursive = true,
                HasAnyProviderId = new Dictionary<string, string> { {ShokoEpisodeId.Name, episodeId } },
            })
            .OfType<Movie>()
            .Where(Lookup.IsEnabledForItem)
            .ToList();

    /// <summary>
    /// Get all episodes with a Shoko Episode ID set across all libraries.
    /// </summary>
    /// <param name="episodeId">Optional. The episode id if we want to filter to only episodes with a given Shoko Episode ID.</param>
    /// <returns>A list of all episodes with a Shoko Episode ID set.</returns>
    public IReadOnlyList<Episode> GetEpisodesFromLibrary(string episodeId = "")
        => LibraryManager
            .GetItemList(new() {
                IncludeItemTypes = [BaseItemKind.Episode],
                HasAnyProviderId = new Dictionary<string, string> { {ShokoEpisodeId.Name, episodeId } },
                IsVirtualItem = false,
                Recursive = true,
            })
            .Cast<Episode>()
            .Where(Lookup.IsEnabledForItem)
            .ToList();

    /// <summary>
    /// Merge all videos with a Shoko Episode ID set.
    /// </summary>
    /// <param name="progress">Progress indicator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async task that will silently complete when the merging is
    /// complete.</returns>
    public async Task SplitAndMergeVideos<TVideo>(
        IReadOnlyList<TVideo> videos,
        IProgress<double>? progress = null,
        CancellationToken? cancellationToken = null
    ) where TVideo : Video
    {
        // Split up any existing merged videos.
        double currentCount = 0d;
        double totalCount = videos.Count;
        foreach (var video in videos) {
            // Handle cancellation and update progress.
            cancellationToken?.ThrowIfCancellationRequested();
            var percent = currentCount++ / totalCount * 50d;
            progress?.Report(percent);

            // Remove all alternate sources linked to the video.
            await RemoveAlternateSources(video);
        }

        // Merge all videos with more than one version (again).
        var duplicationGroups = videos
            .GroupBy(video => (video.GetTopParent()?.Path, video.GetProviderId(ShokoEpisodeId.Name)))
            .Where(groupBy => groupBy.Count() > 1)
            .ToList();
        currentCount = 0d;
        totalCount = duplicationGroups.Count;
        foreach (var videoGroup in duplicationGroups) {
            // Handle cancellation and update progress.
            cancellationToken?.ThrowIfCancellationRequested();
            var percent = 50d + (currentCount++ / totalCount * 50d);
            progress?.Report(percent);

            // Link the videos together as alternate sources.
            await MergeVideos(videoGroup);
        }

        progress?.Report(100);
    }

    /// <summary>
    /// Split up all existing merged videos with a Shoko Episode ID set.
    /// </summary>
    /// <param name="progress">Progress indicator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async task that will silently complete when the splitting is
    /// complete.</returns>
    public async Task SplitVideos<TVideo>(IReadOnlyList<TVideo> videos, IProgress<double>? progress, CancellationToken? cancellationToken) where TVideo : Video
    {
        // Split up any existing merged videos.
        double currentCount = 0d;
        double totalMovies = videos.Count;
        foreach (var video in videos) {
            // Handle cancellation and update progress.
            cancellationToken?.ThrowIfCancellationRequested();
            var percent = currentCount++ / totalMovies * 100d;
            progress?.Report(percent);

            // Remove all alternate sources linked to the video.
            await RemoveAlternateSources(video);
        }

        progress?.Report(100);
    }

    /// <summary>
    /// Merges multiple videos into a single UI element.
    /// </summary>
    ///
    /// Modified from;
    /// https://github.com/jellyfin/jellyfin/blob/9c97c533eff94d25463fb649c9572234da4af1ea/Jellyfin.Api/Controllers/VideosController.cs#L192
    private static async Task MergeVideos<TVideo>(IEnumerable<TVideo> input) where TVideo : Video
    {
        if (input is not IList<TVideo> videos)
            videos = input.ToList();
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
                video.LinkedAlternateVersions = [];

            // Save the changes back to the repository.
            await video.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None)
                .ConfigureAwait(false);
        }

        primaryVersion.LinkedAlternateVersions = [.. alternateVersionsOfPrimary];
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
    private async Task RemoveAlternateSources<TVideo>(TVideo video) where TVideo : Video
    {
        // Find the primary video.
        if (video.LinkedAlternateVersions.Length == 0) {
            // Ensure we're not running on an unlinked item.
            if (string.IsNullOrEmpty(video.PrimaryVersionId))
                return;

            // Make sure the primary video still exists before we proceed.
            if (LibraryManager.GetItemById(video.PrimaryVersionId) is not TVideo primaryVideo)
                return;
            video = primaryVideo;
        }

        // Remove the link for every linked video.
        foreach (var linkedVideo in video.GetLinkedAlternateVersions())
        {
            linkedVideo.SetPrimaryVersionId(null);
            linkedVideo.LinkedAlternateVersions = [];
            await linkedVideo.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None)
                .ConfigureAwait(false);
        }

        // Remove the link for the primary video.
        video.SetPrimaryVersionId(null);
        video.LinkedAlternateVersions = [];
        await video.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None)
            .ConfigureAwait(false);
    }

    #endregion Shared Methods
}
