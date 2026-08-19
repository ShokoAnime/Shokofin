using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Extensions;
using Shokofin.ExternalIds;
using Shokofin.Tasks;
using Shokofin.Utils;

using FileInfo = Shokofin.API.Info.FileInfo;
using ReleaseSource = Shokofin.API.Models.ReleaseSource;

namespace Shokofin.MergeVersions;

/// <summary>
/// Responsible for merging multiple versions of the same video together into a
/// single UI element (by linking the videos together and letting Jellyfin
/// handle the rest).
/// </summary>
///
/// Based upon;
/// https://github.com/danieladov/jellyfin-plugin-mergeversions
public class MergeVersionsManager {
    /// <summary>
    /// Wait time before logging it's in use.
    /// </summary>
    private const int LockWaitMS = 100;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<MergeVersionsManager> _logger;

    /// <summary>
    /// Library manager. Used to fetch items from the library.
    /// </summary>
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Task manager. Used to schedule background tasks.
    /// </summary>
    private readonly ITaskManager _taskManager;

    /// <summary>
    /// Shoko ID Lookup. Used to check if the plugin is enabled for the videos.
    /// </summary>
    private readonly ShokoIdLookup _lookup;

    /// <summary>
    /// Used to lookup the file info for each video.
    /// </summary>
    private readonly ShokoApiManager _apiManager;

    /// <summary>
    /// Used to clear the <see cref="_runGuard"/> when the
    /// <see cref="UsageTracker.Stalled"/> event is ran.
    /// </summary>
    private readonly UsageTracker _usageTracker;

    public MergeVersionsManager(ILogger<MergeVersionsManager> logger, ILibraryManager libraryManager, ITaskManager taskManager, ShokoIdLookup lookup, ShokoApiManager apiManager, UsageTracker usageTracker) {
        _logger = logger;
        _libraryManager = libraryManager;
        _taskManager = taskManager;
        _lookup = lookup;
        _apiManager = apiManager;
        _usageTracker = usageTracker;
        _usageTracker.Stalled += OnUsageTrackerStalled;
    }

    ~MergeVersionsManager() {
        _usageTracker.Stalled -= OnUsageTrackerStalled;
    }

    private void OnUsageTrackerStalled(object? sender, EventArgs e) {
        Clear();
    }

    public void Clear() {
        if (_episodeIds.Count > 0)
            _taskManager.QueueIfNotRunning<MergeQueuedEpisodesTask>();
        if (_movieIds.Count > 0)
            _taskManager.QueueIfNotRunning<MergeQueuedMoviesTask>();
    }

    #region Episodes

    /// <summary>
    /// Used to lock access to retrieving data from and clearing
    /// <see cref="_episodeIds"/>.
    /// </summary>
    private readonly SemaphoreSlim _episodeLock = new(1, 1);

    /// <summary>
    /// Bag of episode IDs encountered during last scan or refresh. Will be used
    /// after the stalled event is fired to merge the episodes because we cannot
    /// do it during the scan or refresh.
    /// </summary>
    private readonly ConcurrentBag<string> _episodeIds = [];

    public async Task SplitAndMergeAllEpisodes(IProgress<double>? progress = null, CancellationToken cancellationToken = default) {
        try {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await _episodeLock.WaitAsync(LockWaitMS, cancellationToken)) {
                _logger.LogDebug("Episode lock is taken, waiting for our turn.");
                await _episodeLock.WaitAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            _episodeIds.Clear();
            var episodes = GetEpisodesFromLibrary();
            _logger.LogDebug("Checking {Count} episodes if they need to be split or merged.", episodes.Count);
            progress ??= new Progress<double>(report => _logger.LogDebug("Episode Progress: {Progress}", report));
            await SplitAndMergeVideos(episodes, progress, cancellationToken);
            _logger.LogDebug("Finished checking {Count} episodes if they need to be split or merged.", episodes.Count);
            progress?.Report(100d);
        }
        finally {
            _episodeLock.Release();
        }
    }

    public async Task SplitAllEpisodes(IProgress<double>? progress = null, CancellationToken cancellationToken = default) {
        try {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await _episodeLock.WaitAsync(LockWaitMS, cancellationToken)) {
                _logger.LogDebug("Episode lock is taken, waiting for our turn.");
                await _episodeLock.WaitAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            _episodeIds.Clear();
            var episodes = GetEpisodesFromLibrary();
            _logger.LogDebug("Checking {Count} episodes if they need to be split.", episodes.Count);
            progress ??= new Progress<double>(report => _logger.LogDebug("Episode Progress: {Progress}", report));
            await SplitVideos(episodes, progress, cancellationToken);
            _logger.LogDebug("Finished checking {Count} episodes if they need to be split.", episodes.Count);
            progress?.Report(100d);
        }
        finally {
            _episodeLock.Release();
        }
    }

    public async Task SplitAndMergeQueuedEpisodes(IProgress<double>? progress = null, CancellationToken cancellationToken = default) {
        try {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await _episodeLock.WaitAsync(LockWaitMS, cancellationToken)) {
                _logger.LogDebug("Episode lock is taken, waiting for our turn.");
                await _episodeLock.WaitAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var episodeIds = _episodeIds.ToArray();
            _episodeIds.Clear();
            if (episodeIds.Length is 0)
                return;
            var episodes = episodeIds
                .Distinct()
                .SelectMany(GetEpisodesFromLibrary)
                .ToList();
            _logger.LogDebug("Checking {Count} episodes if they need to be split or merged.", episodes.Count);
            progress ??= new Progress<double>(report => _logger.LogDebug("Episode Progress: {Progress}", report));
            await SplitAndMergeVideos(episodes, progress, cancellationToken);
            _logger.LogDebug("Finished checking {Count} episodes if they need to be split or merged.", episodes.Count);
            progress.Report(100d);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Encountered an error splitting or merging episodes.");
        }
        finally {
            _episodeLock.Release();
        }
    }

    public void ScheduleSplitAndMergeEpisodesByEpisodeId(string episodeId)
        => _episodeIds.Add(episodeId);

    #endregion

    #region Movies

    /// <summary>
    /// Used to lock access to retrieving data from and clearing
    /// <see cref="_movieIds"/>.
    /// </summary>
    private readonly SemaphoreSlim _movieLock = new(1, 1);

    /// <summary>
    /// Bag of episode IDs encountered during last scan or refresh. Will be used
    /// after the stalled event is fired to merge the movies because we cannot
    /// do it during the scan or refresh.
    /// </summary>
    private readonly ConcurrentBag<string> _movieIds = [];

    public async Task SplitAndMergeAllMovies(IProgress<double>? progress = null, CancellationToken cancellationToken = default) {
        try {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await _movieLock.WaitAsync(LockWaitMS, cancellationToken)) {
                _logger.LogDebug("Movie lock is taken, waiting for our turn.");
                await _movieLock.WaitAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var movies = GetMoviesFromLibrary();
            _movieIds.Clear();
            _logger.LogDebug("Checking {Count} movies if they need to be split or merged.", movies.Count);
            progress ??= new Progress<double>(report => _logger.LogDebug("Movie Progress: {Progress:0.00F}%", report));
            await SplitAndMergeVideos(movies, progress, cancellationToken);
            _logger.LogDebug("Finished checking {Count} movies if they need to be split or merged.", movies.Count);
            progress?.Report(100d);
        }
        finally {
            _movieLock.Release();
        }
    }

    public async Task SplitAllMovies(IProgress<double>? progress = null, CancellationToken cancellationToken = default) {
        try {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await _movieLock.WaitAsync(LockWaitMS, cancellationToken)) {
                _logger.LogDebug("Movie lock is taken, waiting for our turn.");
                await _movieLock.WaitAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var movies = GetMoviesFromLibrary();
            _movieIds.Clear();
            _logger.LogDebug("Checking {Count} movies if they need to be split.", movies.Count);
            progress ??= new Progress<double>(report => _logger.LogDebug("Movie Progress: {Progress:0.00F}%", report));
            await SplitVideos(GetMoviesFromLibrary(), progress, cancellationToken);
            _logger.LogDebug("Finished checking {Count} movies if they need to be split.", movies.Count);
            progress?.Report(100d);
        }
        finally {
            _movieLock.Release();
        }
    }

    public async Task SplitAndMergeQueuedMovies(IProgress<double>? progress = null, CancellationToken cancellationToken = default) {
        try {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await _movieLock.WaitAsync(LockWaitMS, cancellationToken)) {
                _logger.LogDebug("Movie lock is taken, waiting for our turn.");
                await _movieLock.WaitAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var movieEpisodeIds = _movieIds.ToArray();
            _movieIds.Clear();
            if (movieEpisodeIds.Length is 0)
                return;
            var movies = movieEpisodeIds
                .Distinct()
                .SelectMany(GetMoviesFromLibrary)
                .ToList();
            _logger.LogDebug("Checking {Count} movies if they need to be split or merged.", movies.Count);
            progress ??= new Progress<double>(report => _logger.LogDebug("Movie Progress: {Progress:0.00F}%", report));
            await SplitAndMergeVideos(movies, progress, cancellationToken);
            _logger.LogDebug("Finished checking {Count} movies if they need to be split or merged.", movies.Count);
            progress.Report(100d);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Encountered an error splitting or merging movies.");
        }
        finally {
            _movieLock.Release();
        }
    }

    public void ScheduleSplitAndMergeMoviesByEpisodeId(string movieId)
        => _movieIds.Add(movieId);

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
                SourceTypes = [SourceType.Library],
                IsVirtualItem = false,
                Recursive = true,
                HasAnyProviderId = new Dictionary<string, string> { {ProviderNames.ShokoEpisode, episodeId } },
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
                SourceTypes = [SourceType.Library],
                HasAnyProviderId = new Dictionary<string, string> { {ProviderNames.ShokoEpisode, episodeId } },
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
    ) where TVideo : Video {
        // Split up any existing merged videos.
        double currentCount = 0d;
        double totalCount = videos.Count;
        var visitedVideos = new HashSet<Guid>();
        var duplicationGroups = videos
            .GroupBy(video => (video.GetTopParent()?.Path, video.GetProviderId(ProviderNames.ShokoEpisode)))
            .Where(groupBy => groupBy.Count() > 1)
            .ToList();
        var processVideos = duplicationGroups
            .SelectMany(groupBy => groupBy)
            .Select(video => video.Id)
            .ToHashSet();
        foreach (var video in videos) {
            // Handle cancellation and update progress.
            cancellationToken?.ThrowIfCancellationRequested();
            var percent = currentCount++ / totalCount * 50d;
            progress?.Report(percent);

            // Remove all alternate sources linked to the videos we're not processing.
            await CleanVideo(video, visitedVideos, toSkip: processVideos);
        }

        // Correctly merge all videos with more than one version available.
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
    public async Task SplitVideos<TVideo>(IReadOnlyList<TVideo> videos, IProgress<double>? progress, CancellationToken? cancellationToken) where TVideo : Video {
        // Split up any existing merged videos.
        double currentCount = 0d;
        double totalMovies = videos.Count;
        var toSkipVideos = new HashSet<Guid>();
        var visitedVideos = new HashSet<Guid>();
        foreach (var video in videos) {
            // Handle cancellation and update progress.
            cancellationToken?.ThrowIfCancellationRequested();
            var percent = currentCount++ / totalMovies * 100d;
            progress?.Report(percent);

            // Remove all alternate sources linked to the video.
            await CleanVideo(video, visitedVideos, toSkipVideos);
        }

        progress?.Report(100);
    }

    /// <summary>
    /// Merges multiple videos into a single UI element.
    /// </summary>
    ///
    /// Modified from;
    /// https://github.com/jellyfin/jellyfin/blob/9c97c533eff94d25463fb649c9572234da4af1ea/Jellyfin.Api/Controllers/VideosController.cs#L192
    private async Task MergeVideos<TVideo>(IEnumerable<TVideo> input) where TVideo : Video {
        if (input is not List<TVideo> videos)
            videos = [.. input];
        if (videos is not { Count: > 1 })
            return;

        var orderedVideos = await OrderVideos(videos);
        var (primaryVideo, primarySortName) = orderedVideos[0];

        // Process the other videos and link them to the primary video if
        // they're not already linked.
        var updated = false;
        var alternateVersions = new List<LinkedChild>();
        foreach (var (video, sortName) in orderedVideos.Skip(1)) {
            if (alternateVersions.Any(i => i.ItemId == video.Id)) {
                _logger.LogTrace("Skipping already linked alternate version. (PrimaryVideo={PrimaryVideoId},Video={VideoId})", primaryVideo.Id, video.Id);
                continue;
            }

            // Conditionally save the changes back to the repository.
            _logger.LogTrace("Found a new linked alternate version. (PrimaryVideo={PrimaryVideoId},Video={VideoId})", primaryVideo.Id, video.Id);
            alternateVersions.Add(new() {
#if NET10_0_OR_GREATER
                Type = LinkedChildType.LinkedAlternateVersion,
#else
                Path = video.Path,
#endif
                ItemId = video.Id,
            });
            updated = false;
#if NET10_0_OR_GREATER
            if (video.PrimaryVersionId != primaryVideo.Id) {
                video.SetPrimaryVersionId(primaryVideo.Id);
#else
            if (video.PrimaryVersionId != primaryVideo.Id.ToString("N", CultureInfo.InvariantCulture)) {
                video.SetPrimaryVersionId(primaryVideo.Id.ToString("N", CultureInfo.InvariantCulture));
#endif
                updated = true;
            }
            if (!string.Equals(video.ForcedSortName, sortName, StringComparison.Ordinal)) {
                video.ForcedSortName = sortName;
                updated = true;
            }
            if (video.LocalAlternateVersions.Length > 0) {
                video.LocalAlternateVersions = [];
                updated = true;
            }
            if (video.LinkedAlternateVersions.Length > 0) {
                video.LinkedAlternateVersions = [];
                updated = true;
            }
            if (updated) {
                _logger.LogDebug("Saving linked video changes. (PrimaryVideo={PrimaryVideoId},Video={VideoId})", primaryVideo.Id, video.Id);
                await video.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None);
            }
#if NET10_0_OR_GREATER
            // Alternate videos are hidden after merging. Keep playlists and
            // collections valid by pointing their manual links at the primary.
            await _libraryManager.RerouteLinkedChildReferencesAsync(video.Id, primaryVideo.Id);
#endif
        }

        // Keep the alternate sources in a stable order. The UI list is
        // (partially) ordered by the forced sort name, so this won't affect it.
        alternateVersions = [.. alternateVersions.OrderBy(i => i.ItemId)];

        // Conditionally save the changes back to the repository.
        _logger.LogTrace("Found primary video with {Count} linked alternate versions. (PrimaryVideo={PrimaryVideoId})", alternateVersions.Count, primaryVideo.Id);
        updated = false;
        if (primaryVideo.PrimaryVersionId is not null) {
            primaryVideo.SetPrimaryVersionId(null);
            updated = true;
        }
        if (!string.Equals(primaryVideo.ForcedSortName, primarySortName, StringComparison.Ordinal)) {
            primaryVideo.ForcedSortName = primarySortName;
            updated = true;
        }
        if (primaryVideo.LocalAlternateVersions.Length > 0) {
            primaryVideo.LocalAlternateVersions = [];
            updated = true;
        }
        if (primaryVideo.LinkedAlternateVersions.Length != alternateVersions.Count || !primaryVideo.LinkedAlternateVersions.SequenceEqual(alternateVersions, LinkedChildComparer.Instance)) {
            primaryVideo.LinkedAlternateVersions = [..alternateVersions];
            updated = true;
        }
        if (updated) {
            _logger.LogDebug("Saving primary video changes with {Count} linked alternate versions. (PrimaryVideo={PrimaryVideoId})", alternateVersions.Count, primaryVideo.Id);
            await primaryVideo.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None);
        }
    }

    /// <summary>
    /// Removes all alternate video sources from a video and all it's linked
    /// videos.
    /// </summary>
    /// <param name="video">The primary video to clean up.</param>
    /// <param name="visited">A set of video IDs that have already been visited.</param>
    /// <param name="depth">The current depth of recursion. Used for logging.</param>
    /// <typeparam name="TVideo">The type of the video.</typeparam>
    /// <returns>A task that completes when all alternate video sources have been
    /// removed.</returns>
    private async Task CleanVideo<TVideo>(TVideo? video, HashSet<Guid> visited, HashSet<Guid> toSkip, int depth = 0) where TVideo : Video {
        if (video is null)
            return;

        // Only visit a video once per run.
        if (!visited.Add(video.Id)) {
            _logger.LogTrace("Skipping already visited video. (Video={VideoId},Depth={Depth})", video.Id, depth);
            return;
        }

        // Visit the primary video if this is not the primary video.
        if (video.PrimaryVersionId is { } primaryVersionId) {
            var primaryVideo = _libraryManager.GetItemById(primaryVersionId) as TVideo;
            if (primaryVideo is not null) {
                _logger.LogTrace("Found primary video to clean up first. (Video={VideoId},Depth={Depth})", primaryVideo.Id, depth);
                await CleanVideo(primaryVideo, visited, toSkip, depth + 1);
            }
        }

        // Visit every linked video.
        if (video.GetLinkedAlternateVersions().ToList() is { Count: > 0 } linkedAlternateVersions) {
            _logger.LogTrace("Removing {Count} linked alternate sources for video. (Video={VideoId},Depth={Depth})", linkedAlternateVersions.Count, video.Id, depth);
            foreach (var linkedVideo in linkedAlternateVersions) {
                await CleanVideo(linkedVideo, visited, toSkip, depth + 1);
            }
        }

        // Visit every local linked video.
        if (video.GetLocalAlternateVersionIds().Select(id => _libraryManager.GetItemById(id) as TVideo).WhereNotNull().ToList() is { Count: > 0 } localAlternateVersions) {
            _logger.LogTrace("Removing {Count} local alternate sources for video. (Video={VideoId},Depth={Depth})", localAlternateVersions.Count, video.Id, depth);
            foreach (var linkedVideo in localAlternateVersions) {
                await CleanVideo(linkedVideo, visited, toSkip, depth + 1);
            }
        }

        // Skip cleaning this video if it's in the skip list.
        if (toSkip?.Contains(video.Id) ?? false) {
            _logger.LogTrace("Skipped cleaning video. (Video={VideoId},Depth={Depth})", video.Id, depth);
            return;
        }

        // Clean the current video if it's not already clean.
        if (video.PrimaryVersionId is { } || video.ForcedSortName is not null || video.LinkedAlternateVersions.Length > 0 || video.LocalAlternateVersions.Length > 0) {
            _logger.LogTrace("Cleaning up video. (PrimaryVideo={PrimaryVideoId},Video={VideoId},Depth={Depth})", video.PrimaryVersionId, video.Id, depth);
            video.SetPrimaryVersionId(null);
            video.ForcedSortName = null;
            video.LocalAlternateVersions = [];
            video.LinkedAlternateVersions = [];
            await video.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None);
        }
        else {
            _logger.LogTrace("Video is already clean. (PrimaryVideo={PrimaryVideoId},Video={VideoId},Depth={Depth})", video.PrimaryVersionId, video.Id, depth);
        }
    }

    private static MergeVersionSortSelector[] GetOrderedSelectors()
        => Plugin.Instance.Configuration.MergeVersionSortSelectorOrder.Where((t) => Plugin.Instance.Configuration.MergeVersionSortSelectorList.Contains(t)).ToArray();

    private async Task<IList<(TVideo video, string? sortName)>> OrderVideos<TVideo>(IList<TVideo> list) where TVideo : Video {
        var selectors = GetOrderedSelectors();
        return (await Task.WhenAll(list.Select(async video => (video, sortName: await GetSortName(video, selectors)))))
            .OrderBy(tuple => tuple.sortName is null)
            .ThenBy(tuple => tuple.sortName)
            .ThenBy(tuple => tuple.video.Path)
            .ToList();
    }

    private async Task<string?> GetSortName<TVideo>(TVideo video, IList<MergeVersionSortSelector> selectors) where TVideo : Video {
        if (selectors.Count is 0)
            return null;

        var (fileInfo, _, _) = await _apiManager.GetFileInfoByPath(video.Path);
        if (fileInfo is null)
            return null;

        return selectors
            .Select(selector => GetSelectedSortValue(video, fileInfo, selector))
            .Join(".");
    }

    private string GetSelectedSortValue<TVideo>(TVideo video, FileInfo fileInfo, MergeVersionSortSelector selector) where TVideo : Video
        => selector switch {
            MergeVersionSortSelector.ImportedAt => (fileInfo.Shoko.ImportedAt ?? fileInfo.Shoko.CreatedAt).ToUniversalTime().ToString("O"),
            MergeVersionSortSelector.CreatedAt => fileInfo.Shoko.CreatedAt.ToString("O"),
            MergeVersionSortSelector.Resolution => video.GetDefaultVideoStream() is { } videoStream
                ? ((int)Math.Ceiling(((decimal)(videoStream.Width ?? 1) * (videoStream.Height ?? 1)) / 100)).ToString("00000000")
                : "99999999",
            MergeVersionSortSelector.ReleaseGroupName => fileInfo.Shoko.Release?.Group is { } releaseGroup
                ? (
                    !string.IsNullOrEmpty(releaseGroup.ShortName)
                        ? releaseGroup.ShortName
                        : !string.IsNullOrEmpty(releaseGroup.Name)
                            ? releaseGroup.Name
                            : $"_____Release group {releaseGroup.Id}"
                ).ReplaceInvalidPathCharacters()
                : "_____No Group",
            MergeVersionSortSelector.FileSource => fileInfo.Shoko.Release?.Source switch {
                ReleaseSource.BluRay => "01",
                ReleaseSource.Web => "02",
                ReleaseSource.DVD => "03",
                ReleaseSource.VCD => "04",
                ReleaseSource.LaserDisc => "05",
                ReleaseSource.TV => "06",
                ReleaseSource.VHS => "07",
                ReleaseSource.Camera => "08",
                ReleaseSource.Other => "09",
                _ => "FF",
            },
            MergeVersionSortSelector.FileVersion => (10 - fileInfo.Shoko.Release?.Version ?? 1).ToString("0"),
            MergeVersionSortSelector.RelativeDepth => fileInfo.Shoko.Locations
                .Select(i => i.RelativePath.Split(Path.DirectorySeparatorChar).Length)
                .Max()
                .ToString("00"),
            MergeVersionSortSelector.NoVariation => fileInfo.Shoko.IsVariation ? "1" : "0",
            _ => string.Empty,
        };

  internal class LinkedChildComparer : IEqualityComparer<LinkedChild>
  {
    private static LinkedChildComparer? _instance;

    public static LinkedChildComparer Instance => _instance ??= new LinkedChildComparer();

    public bool Equals(LinkedChild? x, LinkedChild? y)
        => x is not null && y is not null && GetHashCode(x) == GetHashCode(y);

    public int GetHashCode([DisallowNull] LinkedChild obj)
#if NET10_0_OR_GREATER
        => HashCode.Combine(obj.ItemId, obj.Type);
#else
        => HashCode.Combine(obj.Path, obj.LibraryItemId, obj.Type, obj.ItemId);
#endif
  }

  #endregion Shared Methods
}
