using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Jellyfin.Data.Enums;
using System.Globalization;
using MediaBrowser.Model.Entities;
using MediaBrowser.Common.Progress;

namespace Shokofin.MergeVersions;

public class MergeVersionsManager
{
    private readonly ILibraryManager LibraryManager;

    private readonly IIdLookup Lookup;

    private readonly ILogger<MergeVersionsManager> Logger;

    public MergeVersionsManager(ILibraryManager libraryManager, IIdLookup lookup, ILogger<MergeVersionsManager> logger)
    {
        LibraryManager = libraryManager;
        Lookup = lookup;
        Logger = logger;
    }

    #region Shared

    public async Task MergeAll(IProgress<double> progress, CancellationToken cancellationToken, bool canSplit = true)
    {
        // Shared progress;
        double episodeProgressValue = 0d, movieProgressValue = 0d;

        // Setup the movie task.
        var movieProgress = new ActionableProgress<double>();
        movieProgress.RegisterAction(value => {
            movieProgressValue = value / 2d;
            progress?.Report(movieProgressValue + episodeProgressValue);
        });
        var movieTask = MergeMovies(movieProgress, cancellationToken, canSplit);

        // Setup the episode task.
        var episodeProgress = new ActionableProgress<double>();
        episodeProgress.RegisterAction(value => {
            episodeProgressValue = value / 2d;
            progress?.Report(movieProgressValue + episodeProgressValue);
            progress?.Report(50d + (value / 2d));
        });
        var episodeTask = MergeEpisodes(episodeProgress, cancellationToken, canSplit);

        // Run them in parallel.
        await Task.WhenAll(movieTask, episodeTask);
    }

    public async Task SplitAll(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Shared progress;
        double episodeProgressValue = 0d, movieProgressValue = 0d;

        // Setup the movie task.
        var movieProgress = new ActionableProgress<double>();
        movieProgress.RegisterAction(value => {
            movieProgressValue = value / 2d;
            progress?.Report(movieProgressValue + episodeProgressValue);
        });
        var movieTask = SplitMovies(movieProgress, cancellationToken);

        // Setup the episode task.
        var episodeProgress = new ActionableProgress<double>();
        episodeProgress.RegisterAction(value => {
            episodeProgressValue = value / 2d;
            progress?.Report(movieProgressValue + episodeProgressValue);
            progress?.Report(50d + (value / 2d));
        });
        var episodeTask = SplitEpisodes(episodeProgress, cancellationToken);

        // Run them in parallel.
        await Task.WhenAll(movieTask, episodeTask);
    }

    #endregion Shared
    #region Movies

    private List<Movie> GetMoviesFromLibrary()
    {
        return LibraryManager.GetItemList(new() {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                IsVirtualItem = false,
                Recursive = true,
                HasAnyProviderId = new Dictionary<string, string> { {"Shoko Episode", "" } },
            })
            .Cast<Movie>()
            .Where(Lookup.IsEnabledForItem)
            .ToList();
    }

    public async Task MergeMovies(IEnumerable<Movie> movies)
        => await MergeVideos(movies.Cast<Video>().OrderBy(e => e.Id).ToList());

    public async Task MergeMovies(IProgress<double> progress, CancellationToken cancellationToken, bool canSplit = true)
    {
        if (canSplit && Plugin.Instance.Configuration.EXPERIMENTAL_SplitThenMergeMovies) {
            await SplitAndMergeMovies(progress, cancellationToken);
            return;
        }

        // Merge all movies with more than one version.
        var movies = GetMoviesFromLibrary();
        var duplicationGroups = movies
            .GroupBy(x => x.ProviderIds["Shoko Episode"])
            .Where(x => x.Count() > 1)
            .ToList();
        double currentCount = 0d;
        double totalGroups = duplicationGroups.Count;
        foreach (var movieGroup in duplicationGroups) {
            // Handle cancelation and update progress.
            cancellationToken.ThrowIfCancellationRequested();
            var percent = (currentCount++ / totalGroups) * 100;
            progress?.Report(percent);

            // Link the movies together as alternate sources.
            await MergeMovies(movieGroup);
        }

        progress?.Report(100);
    }

    public async Task SplitMovies(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Split up any existing merged movies.
        var movies = GetMoviesFromLibrary();
        double currentCount = 0d;
        double totalMovies = movies.Count;
        foreach (var movie in movies) {
            // Handle cancelation and update progress.
            cancellationToken.ThrowIfCancellationRequested();
            var percent = (currentCount++ / totalMovies) * 100d;
            progress?.Report(percent);

            // Remove all alternate sources linked to the movie.
            await RemoveAlternateSources(movie);
        }

        progress?.Report(100);
    }

    private async Task SplitAndMergeMovies(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Split up any existing merged movies.
        var movies = GetMoviesFromLibrary();
        double currentCount = 0d;
        double totalCount = movies.Count;
        foreach (var movie in movies) {
            // Handle cancelation and update progress.
            cancellationToken.ThrowIfCancellationRequested();
            var percent = (currentCount++ / totalCount) * 50d;
            progress?.Report(percent);

            // Remove all alternate sources linked to the movie.
            await RemoveAlternateSources(movie);
        }

        // Merge all movies with more than one version (again).
        var duplicationGroups = movies
            .GroupBy(x => x.ProviderIds["Shoko Episode"])
            .Where(x => x.Count() > 1)
            .ToList();
        currentCount = 0d;
        totalCount = duplicationGroups.Count;
        foreach (var movieGroup in duplicationGroups) {
            // Handle cancelation and update progress.
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

    private List<Episode> GetEpisodesFromLibrary()
    {
        return LibraryManager.GetItemList(new() {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                HasAnyProviderId = new Dictionary<string, string> { {"Shoko Episode", "" } },
                IsVirtualItem = false,
                Recursive = true,
            })
            .Cast<Episode>()
            .Where(Lookup.IsEnabledForItem)
            .ToList();
    }

    public async Task MergeEpisodes(IEnumerable<Episode> episodes)
        => await MergeVideos(episodes.Cast<Video>().OrderBy(e => e.Id).ToList());

    public async Task MergeEpisodes(IProgress<double> progress, CancellationToken cancellationToken, bool canSplit = true)
    {
        if (canSplit && Plugin.Instance.Configuration.EXPERIMENTAL_SplitThenMergeMovies) {
            await SplitAndMergeEpisodes(progress, cancellationToken);
            return;
        }

        // Merge episodes with more than one version.
        var episodes = GetEpisodesFromLibrary();
        var duplicationGroups = episodes
            .GroupBy(x => x.ProviderIds["Shoko Episode"])
            .Where(x => x.Count() > 1)
            .ToList();
        double currentCount = 0d;
        double totalGroups = duplicationGroups.Count;
        foreach (var episodeGroup in duplicationGroups) {
            // Handle cancelation and update progress.
            cancellationToken.ThrowIfCancellationRequested();
            var percent = (currentCount++ / totalGroups) * 100d;
            progress?.Report(percent);

            // Link the episodes together as alternate sources.
            await MergeEpisodes(episodeGroup);
        }

        progress?.Report(100);
    }

    public async Task SplitEpisodes(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Split up any existing merged episodes.
        var episodes = GetEpisodesFromLibrary();
        double currentCount = 0d;
        double totalEpisodes = episodes.Count;
        foreach (var e in episodes) {
            // Handle cancelation and update progress.
            cancellationToken.ThrowIfCancellationRequested();
            var percent = (currentCount++ / totalEpisodes) * 100d;
            progress?.Report(percent);

            // Remove all alternate sources linked to the episode.
            await RemoveAlternateSources(e);
        }

        progress?.Report(100);
    }

    private async Task SplitAndMergeEpisodes(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Split up any existing merged episodes.
        var episodes = GetEpisodesFromLibrary();
        double currentCount = 0d;
        double totalCount = episodes.Count;
        foreach (var e in episodes) {
            // Handle cancelation and update progress.
            cancellationToken.ThrowIfCancellationRequested();
            var percent = (currentCount++ / totalCount) * 100d;
            progress?.Report(percent);

            // Remove all alternate sources linked to the episode.
            await RemoveAlternateSources(e);
        }

        // Merge episodes with more than one version (again).
        var duplicationGroups = episodes
            .GroupBy(x => x.ProviderIds["Shoko Episode"])
            .Where(x => x.Count() > 1)
            .ToList();
        currentCount = 0d;
        totalCount = duplicationGroups.Count;
        foreach (var episodeGroup in duplicationGroups) {
            // Handle cancelation and update progress.
            cancellationToken.ThrowIfCancellationRequested();
            var percent = (currentCount++ / totalCount) * 100d;
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
    private async Task MergeVideos(List<Video> videos)
    {
        if (videos.Count < 2)
            return;

        var primaryVersion = videos.FirstOrDefault(i => i.MediaSourceCount > 1 && string.IsNullOrEmpty(i.PrimaryVersionId));
        if (primaryVersion == null)
        {
            primaryVersion = videos
                .OrderBy(i =>
                {
                    if (i.Video3DFormat.HasValue || i.VideoType != VideoType.VideoFile)
                    {
                        return 1;
                    }

                    return 0;
                })
                .ThenByDescending(i => i.GetDefaultVideoStream()?.Width ?? 0)
                .First();
        }

        var alternateVersionsOfPrimary = primaryVersion.LinkedAlternateVersions.ToList();

        foreach (var video in videos.Where(i => !i.Id.Equals(primaryVersion.Id)))
        {
            video.SetPrimaryVersionId(primaryVersion.Id.ToString("N", CultureInfo.InvariantCulture));

            await video.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);

            if (!alternateVersionsOfPrimary.Any(i => string.Equals(i.Path, video.Path, StringComparison.OrdinalIgnoreCase)))
            {
                alternateVersionsOfPrimary.Add(new() {
                    Path = video.Path,
                    ItemId = video.Id,
                });
            }

            foreach (var linkedItem in video.LinkedAlternateVersions)
            {
                if (!alternateVersionsOfPrimary.Any(i => string.Equals(i.Path, linkedItem.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    alternateVersionsOfPrimary.Add(linkedItem);
                }
            }

            if (video.LinkedAlternateVersions.Length > 0)
            {
                video.LinkedAlternateVersions = Array.Empty<LinkedChild>();
                await video.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
            }
        }

        primaryVersion.LinkedAlternateVersions = alternateVersionsOfPrimary.ToArray();
        await primaryVersion.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes alternate video sources from a video
    /// </summary>
    /// <param name="baseItem">The video to clean up.</param>
    ///
    /// Modified from;
    /// https://github.com/jellyfin/jellyfin/blob/9c97c533eff94d25463fb649c9572234da4af1ea/Jellyfin.Api/Controllers/VideosController.cs#L152
    private async Task RemoveAlternateSources(Video video)
    {
        // Find the primary video.
        if (video.LinkedAlternateVersions.Length == 0)
            video = LibraryManager.GetItemById(video.PrimaryVersionId) as Video;

        // Remove the link for every linked video.
        foreach (var linkedVideo in video.GetLinkedAlternateVersions())
        {
            linkedVideo.SetPrimaryVersionId(null);
            linkedVideo.LinkedAlternateVersions = Array.Empty<LinkedChild>();
            await linkedVideo.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
        }

        // Remove the link for the primary video.
        video.SetPrimaryVersionId(null);
        video.LinkedAlternateVersions = Array.Empty<LinkedChild>();
        await video.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
    }
}
