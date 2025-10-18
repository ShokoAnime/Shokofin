using System;
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
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Extensions;
using Shokofin.ExternalIds;
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

    /// <summary>
    /// Used as a lock/guard to prevent multiple runs on the same video until
    /// the <see cref="UsageTracker.Stalled"/> event is ran.
    /// </summary>
    private readonly GuardedMemoryCache _runGuard;

    public MergeVersionsManager(ILogger<MergeVersionsManager> logger, ILibraryManager libraryManager, ShokoIdLookup lookup, ShokoApiManager apiManager, UsageTracker usageTracker) {
        _logger = logger;
        _libraryManager = libraryManager;
        _lookup = lookup;
        _apiManager = apiManager;
        _usageTracker = usageTracker;
        _runGuard = new(logger, new() { }, new() { });

        _usageTracker.Stalled += OnUsageTrackerStalled;
    }

    ~MergeVersionsManager() {
        _usageTracker.Stalled -= OnUsageTrackerStalled;
    }

    private void OnUsageTrackerStalled(object? sender, EventArgs e) {
        Clear();
    }

    public void Clear() {
        _logger.LogDebug("Clearing data…");
        _runGuard.Clear();
    }

    #region Episodes

    public async Task SplitAndMergeAllEpisodes(IProgress<double>? progress, CancellationToken? cancellationToken) {
        await SplitAndMergeVideos(GetEpisodesFromLibrary(), progress, cancellationToken);

        if (!_libraryManager.IsScanRunning)
            Clear();
        progress?.Report(100d);
    }

    public async Task SplitAllEpisodes(IProgress<double>? progress, CancellationToken? cancellationToken) {
        await SplitVideos(GetEpisodesFromLibrary(), progress, cancellationToken);

        if (!_libraryManager.IsScanRunning)
            Clear();
        progress?.Report(100d);
    }

    public Task<bool> SplitAndMergeEpisodesByEpisodeId(string episodeId)
        => _runGuard.GetOrCreateAsync($"episode:{episodeId}", () => SplitAndMergeVideos(GetEpisodesFromLibrary(episodeId)));

    #endregion

    #region Movies

    public async Task SplitAndMergeAllMovies(IProgress<double>? progress, CancellationToken? cancellationToken) {
        await SplitAndMergeVideos(GetMoviesFromLibrary(), progress, cancellationToken);

        if (!_libraryManager.IsScanRunning)
            Clear();
        progress?.Report(100d);
    }

    public async Task SplitAllMovies(IProgress<double>? progress, CancellationToken? cancellationToken) {
        await SplitVideos(GetMoviesFromLibrary(), progress, cancellationToken);

        if (!_libraryManager.IsScanRunning)
            Clear();
        progress?.Report(100d);
    }

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
            await CleanVideo(video, visitedVideos, toSkip: processVideos).ConfigureAwait(false);
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
            await MergeVideos(videoGroup).ConfigureAwait(false);
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
            await CleanVideo(video, visitedVideos, toSkipVideos).ConfigureAwait(false);
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

        var orderedVideos = await OrderVideos(videos).ConfigureAwait(false);
        var (primaryVideo, primarySortName) = orderedVideos[0];

        // Process the other videos and link them to the primary video if
        // they're not already linked.
        var updated = false;
        var alternateVersions = new List<LinkedChild>();
        foreach (var (video, sortName) in orderedVideos.Skip(1)) {
            if (alternateVersions.Any(i => string.Equals(i.Path, video.Path, StringComparison.OrdinalIgnoreCase))) {
                _logger.LogTrace("Skipping already linked alternate version. (PrimaryVideo={PrimaryVideoId},Video={VideoId})", primaryVideo.Id, video.Id);
                continue;
            }

            // Conditionally save the changes back to the repository.
            _logger.LogTrace("Found a new linked alternate version. (PrimaryVideo={PrimaryVideoId},Video={VideoId})", primaryVideo.Id, video.Id);
            alternateVersions.Add(new() {
                Path = video.Path,
                ItemId = video.Id,
            });
            updated = false;
            if (video.PrimaryVersionId != primaryVideo.Id.ToString("N", CultureInfo.InvariantCulture)) {
                video.SetPrimaryVersionId(primaryVideo.Id.ToString("N", CultureInfo.InvariantCulture));
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
                await video.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
            }
        }

        // Order the alternate sources by path, to make sure we always have it
        // in the same order. The UI list is (partially) ordered by the forced
        // sort name, so this won't affect that.
        alternateVersions = [.. alternateVersions.OrderBy(i => i.Path)];

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
            await primaryVideo.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
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
        if (video.PrimaryVersionId is not null) {
            var primaryVideo = _libraryManager.GetItemById(video.PrimaryVersionId) as TVideo;
            if (primaryVideo is not null) {
                _logger.LogTrace("Found primary video to clean up first. (Video={VideoId},Depth={Depth})", primaryVideo.Id, depth);
                await CleanVideo(primaryVideo, visited, toSkip, depth + 1).ConfigureAwait(false);
            }
        }

        // Visit every linked video.
        if (video.GetLinkedAlternateVersions().ToList() is { Count: > 0 } linkedAlternateVersions) {
            _logger.LogTrace("Removing {Count} linked alternate sources for video. (Video={VideoId},Depth={Depth})", linkedAlternateVersions.Count, video.Id, depth);
            foreach (var linkedVideo in linkedAlternateVersions) {
                await CleanVideo(linkedVideo, visited, toSkip, depth + 1).ConfigureAwait(false);
            }
        }

        // Visit every local linked video.
        if (video.GetLocalAlternateVersionIds().Select(id => _libraryManager.GetItemById(id) as TVideo).WhereNotNull().ToList() is { Count: > 0 } localAlternateVersions) {
            _logger.LogTrace("Removing {Count} local alternate sources for video. (Video={VideoId},Depth={Depth})", localAlternateVersions.Count, video.Id, depth);
            foreach (var linkedVideo in localAlternateVersions) {
                await CleanVideo(linkedVideo, visited, toSkip, depth + 1).ConfigureAwait(false);
            }
        }

        // Skip cleaning this video if it's in the skip list.
        if (toSkip?.Contains(video.Id) ?? false) {
            _logger.LogTrace("Skipped cleaning video. (Video={VideoId},Depth={Depth})", video.Id, depth);
            return;
        }

        // Clean the current video if it's not already clean.
        if (!string.IsNullOrEmpty(video.PrimaryVersionId) || video.ForcedSortName is not null || video.LinkedAlternateVersions.Length > 0 || video.LocalAlternateVersions.Length > 0) {
            _logger.LogTrace("Cleaning up video. (PrimaryVideo={PrimaryVideoId},Video={VideoId},Depth={Depth})", video.PrimaryVersionId, video.Id, depth);
            video.SetPrimaryVersionId(null);
            video.ForcedSortName = null;
            video.LocalAlternateVersions = [];
            video.LinkedAlternateVersions = [];
            await video.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
        }
        else {
            _logger.LogTrace("Video is already clean. (PrimaryVideo={PrimaryVideoId},Video={VideoId},Depth={Depth})", video.PrimaryVersionId, video.Id, depth);
        }
    }

    private static MergeVersionSortSelector[] GetOrderedSelectors()
        => Plugin.Instance.Configuration.MergeVersionSortSelectorOrder.Where((t) => Plugin.Instance.Configuration.MergeVersionSortSelectorList.Contains(t)).ToArray();

    private async Task<IList<(TVideo video, string? sortName)>> OrderVideos<TVideo>(IList<TVideo> list) where TVideo : Video {
        var selectors = GetOrderedSelectors();
        return (await Task.WhenAll(list.Select(async video => (video, sortName: await GetSortName(video, selectors)))).ConfigureAwait(false))
            .OrderBy(tuple => tuple.sortName is null)
            .ThenBy(tuple => tuple.sortName)
            .ThenBy(tuple => tuple.video.Path)
            .ToList();
    }

    private async Task<string?> GetSortName<TVideo>(TVideo video, IList<MergeVersionSortSelector> selectors) where TVideo : Video {
        if (selectors.Count is 0)
            return null;

        var (fileInfo, _, _) = await _apiManager.GetFileInfoByPath(video.Path).ConfigureAwait(false);
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
        => HashCode.Combine(obj.Path, obj.LibraryItemId, obj.Type, obj.ItemId);
  }

  #endregion Shared Methods
}
