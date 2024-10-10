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
using Microsoft.Extensions.Logging;
using Shokofin.ExternalIds;
using Shokofin.Utils;

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
    /// Logger.
    /// </summary>
    private readonly ILogger<MergeVersionsManager> _logger;

    /// <summary>
    /// Library manager. Used to fetch items from the library.
    /// </summary>
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Shoko ID Lookup. Used to check if the plugin is enabled for the videos.
    /// </summary>
    private readonly IIdLookup _lookup;

    /// <summary>
    /// Used to clear the <see cref="_runGuard"/> when the
    /// <see cref="UsageTracker.Stalled"/> event is ran.
    /// </summary>
    private readonly UsageTracker _usageTracker;

    /// <summary>
    /// Used as a lock/guard to prevent multiple runs on the same video until
    /// the <see cref="UsageTracker.Stalled"/> event is ran.
    /// </summary>
    private readonly GuardedMemoryCache _runGuard;

    /// <summary>
    /// Used by the DI IoC to inject the needed interfaces.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="lookup">Shoko ID Lookup.</param>
    public MergeVersionsManager(ILogger<MergeVersionsManager> logger, ILibraryManager libraryManager, IIdLookup lookup, UsageTracker usageTracker)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _lookup = lookup;
        _usageTracker = usageTracker;
        _usageTracker.Stalled += OnUsageTrackerStalled;
        _runGuard = new(logger, new() { }, new() { });
    }

    ~MergeVersionsManager()
    {
        _usageTracker.Stalled -= OnUsageTrackerStalled;
    }

    private void OnUsageTrackerStalled(object? sender, EventArgs e)
    {
        _runGuard.Clear();
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

    public Task<bool> SplitAndMergeEpisodesByEpisodeId(string episodeId)
        => _runGuard.GetOrCreateAsync($"episode:{episodeId}", () => SplitAndMergeVideos(GetEpisodesFromLibrary(episodeId)));

    #endregion

    #region Movie Level

    public async Task SplitAndMergeAllMovies(IProgress<double>? progress, CancellationToken? cancellationToken)
        => await SplitAndMergeVideos(GetMoviesFromLibrary(), progress, cancellationToken);

    public async Task SplitAllMovies(IProgress<double>? progress, CancellationToken? cancellationToken)
        => await SplitVideos(GetMoviesFromLibrary(), progress, cancellationToken);

    public Task<bool> SplitAndMergeMoviesByEpisodeId(string movieId)
        => _runGuard.GetOrCreateAsync($"movie:{movieId}", () => SplitAndMergeVideos(GetMoviesFromLibrary(movieId)));

    #endregion

    #region Shared Methods

    /// <summary>
    /// Get all movies with a Shoko Episode ID set across all libraries.
    /// </summary>
    /// <param name="episodeId">Optional. The episode id if we want to filter to only movies with a given Shoko Episode ID.</param>
    /// <returns>A list of all movies with the given <paramref name="episodeId"/> set.</returns>
    public IReadOnlyList<Movie> GetMoviesFromLibrary(string episodeId = "")
        => _libraryManager
            .GetItemList(new() {
                IncludeItemTypes = [BaseItemKind.Movie],
                IsVirtualItem = false,
                Recursive = true,
                HasAnyProviderId = new Dictionary<string, string> { {ShokoEpisodeId.Name, episodeId } },
            })
            .OfType<Movie>()
            .Where(_lookup.IsEnabledForItem)
            .ToList();

    /// <summary>
    /// Get all episodes with a Shoko Episode ID set across all libraries.
    /// </summary>
    /// <param name="episodeId">Optional. The episode id if we want to filter to only episodes with a given Shoko Episode ID.</param>
    /// <returns>A list of all episodes with a Shoko Episode ID set.</returns>
    public IReadOnlyList<Episode> GetEpisodesFromLibrary(string episodeId = "")
        => _libraryManager
            .GetItemList(new() {
                IncludeItemTypes = [BaseItemKind.Episode],
                HasAnyProviderId = new Dictionary<string, string> { {ShokoEpisodeId.Name, episodeId } },
                IsVirtualItem = false,
                Recursive = true,
            })
            .Cast<Episode>()
            .Where(_lookup.IsEnabledForItem)
            .ToList();

    /// <summary>
    /// Merge all videos with a Shoko Episode ID set.
    /// </summary>
    /// <param name="progress">Progress indicator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async task that will silently complete when the merging is
    /// complete.</returns>
    public async Task<bool> SplitAndMergeVideos<TVideo>(
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

        return true;
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
    private async Task MergeVideos<TVideo>(IEnumerable<TVideo> input) where TVideo : Video
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
        var alternateVersionsOfPrimary = primaryVersion.LinkedAlternateVersions.ToList();
        foreach (var video in videos.Where(v => !v.Id.Equals(primaryVersion.Id)))
        {
            video.SetPrimaryVersionId(primaryVersion.Id.ToString("N", CultureInfo.InvariantCulture));
            if (!alternateVersionsOfPrimary.Any(i => string.Equals(i.Path, video.Path, StringComparison.OrdinalIgnoreCase))) {
                _logger.LogTrace("Adding linked alternate version. (PrimaryVideo={PrimaryVideoId},Video={VideoId})", primaryVersion.Id, video.Id);
                alternateVersionsOfPrimary.Add(new() {
                    Path = video.Path,
                    ItemId = video.Id,
                });
            }

            foreach (var linkedItem in video.LinkedAlternateVersions) {
                if (!alternateVersionsOfPrimary.Any(i => string.Equals(i.Path, linkedItem.Path, StringComparison.OrdinalIgnoreCase))) {
                    _logger.LogTrace("Adding linked alternate version. (PrimaryVideo={PrimaryVideoId},Video={VideoId},LinkedVideo={LinkedVideoId})", primaryVersion.Id, video.Id, linkedItem.ItemId);
                    alternateVersionsOfPrimary.Add(linkedItem);
                }
            }

            // Reset the linked alternate versions for the linked videos.
            if (video.LinkedAlternateVersions.Length > 0) {
                _logger.LogTrace("Resetting linked alternate versions for video. (Video={VideoId})", video.Id);
                video.LinkedAlternateVersions = [];
            }

            // Save the changes back to the repository.
            await video.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None)
                .ConfigureAwait(false);
        }

        _logger.LogTrace("Saving {Count} linked alternate versions. (PrimaryVideo={PrimaryVideoId})", alternateVersionsOfPrimary.Count, primaryVersion.Id);
        primaryVersion.LinkedAlternateVersions = [.. alternateVersionsOfPrimary.OrderBy(i => i.Path)];
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
            if (_libraryManager.GetItemById(video.PrimaryVersionId) is not TVideo primaryVideo)
                return;
    
            _logger.LogTrace("Primary video found for video. (PrimaryVideo={PrimaryVideoId},Video={VideoId})", primaryVideo.Id, video.Id);
            video = primaryVideo;
        }

        // Remove the link for every linked video.
        var linkedAlternateVersions = video.GetLinkedAlternateVersions().ToList();
        _logger.LogTrace("Removing {Count} alternate sources for video. (Video={VideoId})", linkedAlternateVersions.Count, video.Id);
        foreach (var linkedVideo in linkedAlternateVersions) {
            if (string.IsNullOrEmpty(linkedVideo.PrimaryVersionId))
                continue;

            _logger.LogTrace("Removing alternate source. (PrimaryVideo={PrimaryVideoId},Video={VideoId})", linkedVideo.PrimaryVersionId, video.Id);
            linkedVideo.SetPrimaryVersionId(null);
            linkedVideo.LinkedAlternateVersions = [];
            await linkedVideo.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None)
                .ConfigureAwait(false);
        }

        // Remove the link for the primary video.
        if (!string.IsNullOrEmpty(video.PrimaryVersionId)) {
            _logger.LogTrace("Removing primary source. (PrimaryVideo={PrimaryVideoId},Video={VideoId})", video.PrimaryVersionId, video.Id);
            video.SetPrimaryVersionId(null);
            video.LinkedAlternateVersions = [];
            await video.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    #endregion Shared Methods
}
