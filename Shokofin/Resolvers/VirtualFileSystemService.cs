using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Naming.Common;
using Emby.Naming.ExternalFiles;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Models;
using Shokofin.Configuration;
using Shokofin.Extensions;
using Shokofin.ExternalIds;
using Shokofin.Resolvers.Models;
using Shokofin.Utils;

using File = System.IO.File;

namespace Shokofin.Resolvers;

public class VirtualFileSystemService {
    private readonly ShokoApiManager ApiManager;

    private readonly ShokoApiClient ApiClient;

    private readonly ILibraryManager LibraryManager;

    private readonly IServerConfigurationManager ConfigurationManager;

    private readonly IFileSystem FileSystem;

    private readonly ILogger<VirtualFileSystemService> Logger;

    private readonly MediaFolderConfigurationService ConfigurationService;

    private readonly NamingOptions NamingOptions;

    private readonly ExternalPathParser ExternalSubtitlePathParser;

    private readonly ExternalPathParser ExternalAudioPathParser;

    private readonly GuardedMemoryCache DataCache;

    // Note: Out of the 14k entries in my test shoko database, then only **348** entries have a title longer than 64 characters.
    private const int NameCutOff = 64;

    private static readonly HashSet<string> IgnoreFolderNames = [
        "backdrops",
        "behind the scenes",
        "deleted scenes",
        "interviews",
        "scenes",
        "samples",
        "shorts",
        "featurettes",
        "clips",
        "other",
        "extras",
        "trailers",
    ];

    public VirtualFileSystemService(
        ShokoApiManager apiManager,
        ShokoApiClient apiClient,
        MediaFolderConfigurationService configurationService,
        ILibraryManager libraryManager,
        IServerConfigurationManager configurationManager,
        IFileSystem fileSystem,
        ILogger<VirtualFileSystemService> logger,
        ILocalizationManager localizationManager,
        NamingOptions namingOptions
    ) {
        ApiManager = apiManager;
        ApiClient = apiClient;
        ConfigurationService = configurationService;
        LibraryManager = libraryManager;
        ConfigurationManager = configurationManager;
        FileSystem = fileSystem;
        Logger = logger;
        DataCache = new(logger, new() { ExpirationScanFrequency = TimeSpan.FromMinutes(25) }, new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1), SlidingExpiration = TimeSpan.FromMinutes(15) });
        NamingOptions = namingOptions;
        ExternalSubtitlePathParser = new ExternalPathParser(namingOptions, localizationManager, MediaBrowser.Model.Dlna.DlnaProfileType.Subtitle);
        ExternalAudioPathParser = new ExternalPathParser(namingOptions, localizationManager, MediaBrowser.Model.Dlna.DlnaProfileType.Audio);
        Plugin.Instance.Tracker.Stalled += OnTrackerStalled;
    }

    ~VirtualFileSystemService() {
        Plugin.Instance.Tracker.Stalled -= OnTrackerStalled;
        DataCache.Dispose();
    }

    private void OnTrackerStalled(object? sender, EventArgs eventArgs)
        => Clear();

    public void Clear() {
        Logger.LogDebug("Clearing data…");
        DataCache.Clear();
    }

    #region Preview Structure

    public async Task<(HashSet<string> filesBefore, HashSet<string> filesAfter, VirtualFolderInfo? virtualFolder, LinkGenerationResult? result, string vfsPath)> PreviewChangesForLibrary(Guid libraryId) {
        // Don't allow starting a preview if a library scan is running.

        var virtualFolders = LibraryManager.GetVirtualFolders();
        var selectedFolder = virtualFolders.FirstOrDefault(folder => Guid.TryParse(folder.ItemId, out var guid) && guid == libraryId);
        if (selectedFolder is null)
            return ([], [], null, null, string.Empty);

        if (LibraryManager.FindByPath(selectedFolder.Locations[0], true) is not Folder mediaFolder)
            return ([], [], selectedFolder, null, string.Empty);

        var collectionType = selectedFolder.CollectionType.ConvertToCollectionType();
        var (vfsPath, _, mediaConfigs, _) = await ConfigurationService.GetMediaFoldersForLibraryInVFS(mediaFolder, collectionType, config => config.IsVirtualFileSystemEnabled).ConfigureAwait(false);
        if (string.IsNullOrEmpty(vfsPath) || mediaConfigs.Count is 0)
            return ([], [], selectedFolder, null, string.Empty);

        if (LibraryManager.IsScanRunning)
            return ([], [], selectedFolder, null, string.Empty);

        // Only allow the preview to run once per caching cycle.
        return await DataCache.GetOrCreateAsync($"preview-changes:{vfsPath}", async () => {
            var fileChecker = GetFileCheckerForMediaFolders(mediaConfigs);
            var allFiles = GetFilesForManagedFolders(mediaConfigs, fileChecker);
            var result = await GenerateStructure(collectionType, vfsPath, allFiles, preview: true).ConfigureAwait(false);
            result += CleanupStructure(vfsPath, vfsPath, result.Paths.ToArray(), preview: true);

            // This call will be slow depending on the size of your collection.
            var existingPaths = FileSystem.DirectoryExists(vfsPath)
                ? FileSystem.GetFilePaths(vfsPath, true).ToHashSet()
                : [];

            // Alter the paths to match the new structure.
            var alteredPaths = existingPaths
                .Concat(result.Paths.ToArray())
                .Except(result.RemovedPaths.ToArray())
                .ToHashSet();

            return (existingPaths, alteredPaths, selectedFolder, result, vfsPath);
        }).ConfigureAwait(false);
    }

    #endregion

    #region Generate Structure

    /// <summary>
    /// Generates the VFS structure if the VFS is enabled for the <paramref name="mediaFolder"/>.
    /// </summary>
    /// <param name="mediaFolder">The media folder to generate a structure for.</param>
    /// <param name="path">The file or folder within the media folder to generate a structure for.</param>
    /// <returns>The VFS path, if it succeeded.</returns>
    public async Task<(string?, bool)> GenerateStructureInVFS(Folder mediaFolder, CollectionType? collectionType, string path) {
        var (vfsPath, mainMediaFolderPath, mediaConfigs, skipGeneration) = await ConfigurationService.GetMediaFoldersForLibraryInVFS(mediaFolder, collectionType, config => config.IsVirtualFileSystemEnabled).ConfigureAwait(false);
        if (string.IsNullOrEmpty(vfsPath) || string.IsNullOrEmpty(mainMediaFolderPath) || mediaConfigs.Count is 0)
            return (null, false);

        if (!Plugin.Instance.CanCreateSymbolicLinks)
            throw new Exception("Windows users are required to enable Developer Mode then restart Jellyfin to be able to create symbolic links, a feature required to use the VFS.");

        var shouldContinue = path.StartsWith(vfsPath + Path.DirectorySeparatorChar) || path == mainMediaFolderPath;
        if (!shouldContinue)
            return (vfsPath, false);

        // Skip link generation if we've already generated for the library.
        if (DataCache.TryGetValue<bool>($"should-skip-vfs-path:{vfsPath}", out var shouldReturnPath))
            return (
                shouldReturnPath ? vfsPath : null,
                true
            );

        // Check full path and all parent directories if they have been indexed.
        if (path.StartsWith(vfsPath + Path.DirectorySeparatorChar)) {
            var pathSegments = path[(vfsPath.Length + 1)..].Split(Path.DirectorySeparatorChar).Prepend(vfsPath).ToArray();
            while (pathSegments.Length > 1) {
                var subPath = Path.Join(pathSegments);
                if (DataCache.TryGetValue<bool>($"should-skip-vfs-path:{subPath}", out _))
                    return (vfsPath, true);
                pathSegments = pathSegments.SkipLast(1).ToArray();
            }
        }

        // Only do this once.
        var key = !path.StartsWith(vfsPath) && mediaConfigs.Any(config => path.StartsWith(config.MediaFolderPath))
            ? $"should-skip-vfs-path:{vfsPath}"
            : $"should-skip-vfs-path:{path}";
        shouldReturnPath = await DataCache.GetOrCreateAsync<bool>(key, async () => {
            Logger.LogInformation(
                "Generating VFS structure for library {LibraryName} at sub-path {Path}. This might take some time depending on your collection size. (Library={LibraryId})",
                mediaConfigs[0].LibraryName,
                path.StartsWith(vfsPath + Path.DirectorySeparatorChar) ? path[vfsPath.Length..] : Path.DirectorySeparatorChar,
                mediaConfigs[0].LibraryId
            );

            // Iterate the files already in the VFS.
            string? pathToClean = null;
            IEnumerable<(string sourceLocation, string fileId, string seriesId)>? allFiles = null;
            if (path.StartsWith(vfsPath + Path.DirectorySeparatorChar)) {
                var fileExists = GetFileCheckerForMediaFolders(mediaConfigs);
                var pathSegments = path[(vfsPath.Length + 1)..].Split(Path.DirectorySeparatorChar);
                switch (pathSegments.Length) {
                    // show/movie-folder level
                    case 1: {
                        var seriesName = pathSegments[0];
                        if (!seriesName.TryGetAttributeValue(ProviderNames.ShokoSeries, out var seasonId))
                            break;

                        // movie-folder
                        if (seriesName.TryGetAttributeValue(ProviderNames.ShokoEpisode, out var episodeId) ) {
                            pathToClean = path;
                            allFiles = GetFilesForMovie(episodeId, seasonId, mediaConfigs, fileExists);
                            break;
                        }

                        // show
                        pathToClean = path;
                        allFiles = GetFilesForShow(seasonId, null, mediaConfigs, fileExists);
                        break;
                    }

                    // season/movie level
                    case 2: {
                        var (seriesName, seasonOrMovieName) = pathSegments;
                        if (!seriesName.TryGetAttributeValue(ProviderNames.ShokoSeries, out var seasonId))
                            break;

                        // movie
                        if (seriesName.TryGetAttributeValue(ProviderNames.ShokoEpisode, out _)) {
                            if (!seasonOrMovieName.TryGetAttributeValue(ProviderNames.ShokoSeries, out var seriesId) || !int.TryParse(seriesId, out _))
                                break;

                            if (!seasonOrMovieName.TryGetAttributeValue(ProviderNames.ShokoFile, out var fileId) || !int.TryParse(fileId, out _))
                                break;

                            allFiles = GetFilesForEpisode(fileId, seriesId, mediaConfigs, fileExists);
                            break;
                        }

                        // "season" or extras
                        if (!seasonOrMovieName.StartsWith("Season ") || !int.TryParse(seasonOrMovieName.Split(' ').Last(), out var seasonNumber))
                            break;

                        pathToClean = path;
                        allFiles = GetFilesForShow(seasonId, seasonNumber, mediaConfigs, fileExists);
                        break;
                    }

                    // episodes level
                    case 3: {
                        var (seriesName, seasonName, episodeName) = pathSegments;
                        if (!seriesName.TryGetAttributeValue(ProviderNames.ShokoSeries, out var seasonId))
                            break;

                        if (!seasonName.StartsWith("Season ") || !int.TryParse(seasonName.Split(' ').Last(), out _))
                            break;

                        if (!episodeName.TryGetAttributeValue(ProviderNames.ShokoSeries, out var seriesId) || !int.TryParse(seriesId, out _))
                            break;

                        if (!episodeName.TryGetAttributeValue(ProviderNames.ShokoFile, out var fileId) || !int.TryParse(fileId, out _))
                            break;

                        allFiles = GetFilesForEpisode(fileId, seriesId, mediaConfigs, fileExists);
                        break;
                    }
                }
            }
            // Iterate files in the "real" media folder.
            else if (mediaConfigs.Any(config => path.StartsWith(config.MediaFolderPath)) || path == vfsPath) {
                var fileChecker = GetFileCheckerForMediaFolders(mediaConfigs);
                pathToClean = vfsPath;
                allFiles = GetFilesForManagedFolders(mediaConfigs, fileChecker);
            }

            if (allFiles is null)
                return false;

            // Skip generation if we're going to (re-)schedule a library scan.
            if (skipGeneration)
                return true;

            // Generate and cleanup the structure in the VFS.
            var result = await GenerateStructure(collectionType, vfsPath, allFiles).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(pathToClean))
                result += CleanupStructure(vfsPath, pathToClean, result.Paths.ToArray());

            // Save which paths we've already generated so we can skip generation
            // for them and their sub-paths later, and also print the result.
            result.Print(Logger, mediaConfigs.Any(config => path.StartsWith(config.MediaFolderPath)) ? vfsPath : path);

            return true;
        }).ConfigureAwait(false);

        return (
            shouldReturnPath ? vfsPath : null,
            true
        );
    }

    private Func<string, bool> GetFileCheckerForMediaFolders(IReadOnlyList<MediaFolderConfiguration> mediaConfigs) {
        if (Plugin.Instance.Configuration.VFS_IterativeFileChecks)
            return FileSystem.FileExists;

        var libraryId = mediaConfigs[0].LibraryId;
        Logger.LogDebug("Looking for files in library across {Count} folders. (Library={LibraryId})", mediaConfigs.Count, libraryId);
        var start = DateTime.UtcNow;
        var paths = new HashSet<string>();
        foreach (var mediaConfig in mediaConfigs) {
            Logger.LogDebug("Looking for files in folder at {Path}. (Library={LibraryId})", mediaConfig.MediaFolderPath, libraryId);
            var folderStart = DateTime.UtcNow;
            var before = paths.Count;
            paths.UnionWith(
                FileSystem.GetFilePaths(mediaConfig.MediaFolderPath, true)
                    .Where(path => NamingOptions.VideoFileExtensions.Contains(Path.GetExtension(path)))
            );
            Logger.LogDebug("Found {FileCount} files in folder at {Path} in {TimeSpan}. (Library={LibraryId})", paths.Count - before, mediaConfig.MediaFolderPath, DateTime.UtcNow - folderStart, libraryId);
        }

        Logger.LogDebug("Found {FileCount} files in library across {Count} in {TimeSpan}. (Library={LibraryId})", paths.Count, mediaConfigs.Count, DateTime.UtcNow - start, libraryId);
        return paths.Contains;
    }

    private IEnumerable<(string sourceLocation, string fileId, string seriesId)> GetFilesForEpisode(string fileId, string seriesId, IReadOnlyList<MediaFolderConfiguration> mediaConfigs, Func<string, bool> fileExists) {
        var totalFiles = 0;
        var start = DateTime.UtcNow;
        var file = ApiClient.GetFile(fileId)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
        if (file is null || !file.CrossReferences.Any(xref => xref.Series.ToString() == seriesId))
            yield break;

        Logger.LogDebug(
            "Iterating files to potentially use within {Count} media folders. (File={FileId},Series={SeriesId},Library={LibraryId})",
            mediaConfigs.Count,
            fileId,
            seriesId,
            mediaConfigs[0].LibraryId
        );

        foreach (var (managedFolderId, managedFolderSubPath, mediaFolderPaths) in mediaConfigs.ToManagedFolderList()) {
            var location = file.Locations
                .Where(location => location.ManagedFolderId == managedFolderId && (managedFolderSubPath.Length is 0 || location.RelativePath.StartsWith(managedFolderSubPath)))
                .FirstOrDefault();
            if (location is null)
                continue;

            foreach (var mediaFolderPath in mediaFolderPaths) {
                var sourceLocation = Path.Join(mediaFolderPath, location.RelativePath[managedFolderSubPath.Length..]);
                if (!fileExists(sourceLocation))
                    continue;

                totalFiles++;
                yield return (sourceLocation, fileId, seriesId);
                goto forLoopBreak;
            }

            continue;
            forLoopBreak: break;
        }

        var timeSpent = DateTime.UtcNow - start;
        Logger.LogDebug(
            "Iterated {Count} file(s) to potentially use within {Count} media folders in {TimeSpan} (File={FileId},Series={SeriesId},Library={LibraryId})",
            totalFiles,
            mediaConfigs.Count,
            timeSpent,
            fileId,
            seriesId,
            mediaConfigs[0].LibraryId
        );
    }

    private IEnumerable<(string sourceLocation, string fileId, string seriesId)> GetFilesForMovie(string episodeId, string seasonId, IReadOnlyList<MediaFolderConfiguration> mediaConfigs, Func<string, bool> fileExists) {
        var start = DateTime.UtcNow;
        var totalFiles = 0;
        var seasonInfo = ApiManager.GetSeasonInfo(seasonId)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
        if (seasonInfo is null)
            yield break;

        Logger.LogDebug(
            "Iterating files to potentially use within {Count} media folders. (Episode={EpisodeId},Season={SeasonId},Library={LibraryId})",
            mediaConfigs.Count,
            episodeId,
            seasonId,
            mediaConfigs[0].LibraryId
        );

        var episodeIds = seasonInfo.ExtrasList.Select(episode => episode.Id).Append(episodeId).ToHashSet();
        var files = seasonInfo.GetFiles()
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
        var fileLocations = files
            .Where(tuple => tuple.episodeIds.Overlaps(episodeIds))
            .SelectMany(tuple => tuple.file.Locations.Select(location => (tuple.file, tuple.seriesId, location)))
            .ToList();
        foreach (var (file, fileSeriesId, location) in fileLocations) {
            foreach (var (managedFolderId, managedFolderSubPath, mediaFolderPaths) in mediaConfigs.ToManagedFolderList()) {
                if (location.ManagedFolderId != managedFolderId || managedFolderSubPath.Length != 0 && !location.RelativePath.StartsWith(managedFolderSubPath))
                    continue;

                foreach (var mediaFolderPath in mediaFolderPaths) {
                    var sourceLocation = Path.Join(mediaFolderPath, location.RelativePath[managedFolderSubPath.Length..]);
                    if (!fileExists(sourceLocation))
                        continue;

                    totalFiles++;
                    yield return (sourceLocation, file.Id.ToString(), fileSeriesId);
                    goto forLoopBreak;
                }

                continue;
                forLoopBreak: break;
            }
        }

        var timeSpent = DateTime.UtcNow - start;
        Logger.LogDebug(
            "Iterated {Count} file(s) to potentially use within {Count} media folders in {TimeSpan} (Episode={EpisodeId},Season={SeasonId},Library={LibraryId})",
            totalFiles,
            mediaConfigs.Count,
            timeSpent,
            episodeId,
            seasonId,
            mediaConfigs[0].LibraryId
        );
    }

    private IEnumerable<(string sourceLocation, string fileId, string seriesId)> GetFilesForShow(string seasonId, int? seasonNumber, IReadOnlyList<MediaFolderConfiguration> mediaConfigs, Func<string, bool> fileExists) {
        var start = DateTime.UtcNow;
        var showInfo = ApiManager.GetShowInfoBySeasonId(seasonId).ConfigureAwait(false).GetAwaiter().GetResult();
        if (showInfo is null)
            yield break;
        Logger.LogDebug(
            "Iterating files to potentially use within {Count} media folders. (MainSeason={MainSeasonId},Season={SeasonNumber},Library={LibraryId})",
            mediaConfigs.Count,
            seasonId,
            seasonNumber,
            mediaConfigs[0].LibraryId
        );

        // Only return the files for the given season.
        var totalFiles = 0;
        var configList = mediaConfigs.ToManagedFolderList();
        if (seasonNumber.HasValue) {
            // Special handling of specials (pun intended)
            if (seasonNumber.Value is 0) {
                foreach (var seasonInfo in showInfo.SeasonList) {
                    var episodeIds = seasonInfo.SpecialsList.Select(episode => episode.Id).ToHashSet();
                    var files = seasonInfo.GetFiles().ConfigureAwait(false).GetAwaiter().GetResult();
                    var fileLocations = files
                        .Where(tuple => tuple.episodeIds.Overlaps(episodeIds))
                        .SelectMany(tuple => tuple.file.Locations.Select(location => (tuple.file, tuple.seriesId, location)))
                        .ToList();
                    foreach (var (file, fileSeriesId, location) in fileLocations) {
                        foreach (var (managedFolderId, managedFolderSubPath, mediaFolderPaths) in configList) {
                            if (location.ManagedFolderId != managedFolderId || managedFolderSubPath.Length != 0 && !location.RelativePath.StartsWith(managedFolderSubPath))
                                continue;

                            foreach (var mediaFolderPath in mediaFolderPaths) {
                                var sourceLocation = Path.Join(mediaFolderPath, location.RelativePath[managedFolderSubPath.Length..]);
                                if (!fileExists(sourceLocation))
                                    continue;

                                totalFiles++;
                                yield return (sourceLocation, file.Id.ToString(), fileSeriesId);
                                goto forLoopBreak;
                            }

                            continue;
                            forLoopBreak: break;
                        }
                    }
                }
            }
            // All other seasons.
            else {
                var seasonInfo = showInfo.GetSeasonInfoBySeasonNumber(seasonNumber.Value);
                if (seasonInfo != null) {
                    var baseNumber = showInfo.GetBaseSeasonNumberForSeasonInfo(seasonInfo);
                    var offset = seasonNumber.Value - baseNumber;
                    var episodeIds = (offset is 0 ? seasonInfo.EpisodeList.Concat(seasonInfo.ExtrasList) : seasonInfo.AlternateEpisodesList).Select(episode => episode.Id).ToHashSet();
                    var files = seasonInfo.GetFiles().ConfigureAwait(false).GetAwaiter().GetResult();
                    var fileLocations = files
                        .Where(tuple => tuple.episodeIds.Overlaps(episodeIds))
                        .SelectMany(tuple => tuple.file.Locations.Select(location => (tuple.file, tuple.seriesId, location)))
                        .ToList();
                    foreach (var (file, fileSeriesId, location) in fileLocations) {
                        foreach (var (managedFolderId, managedFolderSubPath, mediaFolderPaths) in configList) {
                            if (location.ManagedFolderId != managedFolderId || managedFolderSubPath.Length != 0 && !location.RelativePath.StartsWith(managedFolderSubPath))
                                continue;

                            foreach (var mediaFolderPath in mediaFolderPaths) {
                                var sourceLocation = Path.Join(mediaFolderPath, location.RelativePath[managedFolderSubPath.Length..]);
                                if (!fileExists(sourceLocation))
                                    continue;

                                totalFiles++;
                                yield return (sourceLocation, file.Id.ToString(), fileSeriesId);
                                goto forLoopBreak;
                            }

                            continue;
                            forLoopBreak: break;
                        }
                    }
                }
            }
        }
        // Return all files for the show.
        else {
            foreach (var seasonInfo in showInfo.SeasonList) {
                var files = seasonInfo.GetFiles().ConfigureAwait(false).GetAwaiter().GetResult();
                var fileLocations = files
                    .SelectMany(tuple => tuple.file.Locations.Select(location => (tuple.file, tuple.seriesId, location)))
                    .ToList();
                foreach (var (file, fileSeriesId, location) in fileLocations) {
                    foreach (var (managedFolderId, managedFolderSubPath, mediaFolderPaths) in configList) {
                        if (location.ManagedFolderId != managedFolderId || managedFolderSubPath.Length != 0 && !location.RelativePath.StartsWith(managedFolderSubPath))
                            continue;

                        foreach (var mediaFolderPath in mediaFolderPaths) {
                            var sourceLocation = Path.Join(mediaFolderPath, location.RelativePath[managedFolderSubPath.Length..]);
                            if (!fileExists(sourceLocation))
                                continue;

                            totalFiles++;
                            yield return (sourceLocation, file.Id.ToString(), fileSeriesId);
                            goto forLoopBreak;
                        }

                        continue;
                        forLoopBreak: break;
                    }
                }
            }
        }

        var timeSpent = DateTime.UtcNow - start;
        Logger.LogDebug(
            "Iterated {FileCount} files to potentially use within {Count} media folders in {TimeSpan} (MainSeason={MainSeasonId},Season={SeasonNumber},Library={LibraryId})",
            totalFiles,
            mediaConfigs.Count,
            timeSpent,
            seasonId,
            seasonNumber,
            mediaConfigs[0].LibraryId
        );
    }

    private static readonly HashSet<int> AnidbExceptionSet = [
        3651, // Suzumiya Haruhi no Yuuutsu (2006)
    ];

    private IEnumerable<(string sourceLocation, string fileId, string seriesId)> GetFilesForManagedFolders(IReadOnlyList<MediaFolderConfiguration> mediaConfigs, Func<string, bool> fileExists) {
        var start = DateTime.UtcNow;
        var singleSeriesIds = new HashSet<int>();
        var multiSeriesFiles = new List<(API.Models.File, string)>();
        var totalSingleSeriesFiles = 0;
        var libraryId = mediaConfigs[0].LibraryId;
        foreach (var (managedFolderId, managedFolderSubPath, mediaFolderPaths) in mediaConfigs.ToManagedFolderList()) {
            var firstPage = ApiClient.GetFilesInManagedFolder(managedFolderId, managedFolderSubPath);
            var pageData = firstPage
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            var totalPages = pageData.List.Count == pageData.Total ? 1 : (int)Math.Ceiling((float)pageData.Total / pageData.List.Count);
            Logger.LogDebug(
                "Iterating files to potentially use within media folder(s) at {Path} by checking {TotalCount} matches. (LibraryId={LibraryId},ManagedFolder={FolderId},RelativePath={RelativePath},PageSize={PageSize},TotalPages={TotalPages})",
                mediaFolderPaths,
                pageData.Total,
                libraryId,
                managedFolderId,
                managedFolderSubPath,
                pageData.List.Count == pageData.Total ? null : pageData.List.Count,
                totalPages
            );

            // Ensure at most 5 pages are in-flight at any given time, until we're done fetching the pages.
            var semaphore = new SemaphoreSlim(5);
            var pages = new List<Task<ListResult<API.Models.File>>>() { firstPage };
            for (var page = 2; page <= totalPages; page++)
                pages.Add(GetManagedFolderFilesPage(managedFolderId, managedFolderSubPath, page, semaphore));

            do {
                var task = Task.WhenAny(pages).ConfigureAwait(false).GetAwaiter().GetResult();
                pages.Remove(task);
                semaphore.Release();
                pageData = task.Result;

                Logger.LogTrace(
                    "Iterating page {PageNumber} with size {PageSize} (LibraryId={LibraryId},ManagedFolder={FolderId},RelativePath={RelativePath})",
                    totalPages - pages.Count,
                    pageData.List.Count,
                    libraryId,
                    managedFolderId,
                    managedFolderSubPath
                );
                foreach (var file in pageData.List) {
                    if (file.CrossReferences.Count is 0)
                        continue;

                    var location = file.Locations
                        .Where(location => location.ManagedFolderId == managedFolderId && (managedFolderSubPath.Length is 0 || location.RelativePath.StartsWith(managedFolderSubPath)))
                        .FirstOrDefault();
                    if (location is null)
                        continue;

                    foreach (var mediaFolderPath in mediaFolderPaths) {
                        var sourceLocation = Path.Join(mediaFolderPath, location.RelativePath[managedFolderSubPath.Length..]);
                        if (!fileExists(sourceLocation))
                            continue;

                        // Yield all single-series files now, and offset the processing of all multi-series files for later.
                        var seriesIds = file.CrossReferences.Where(x => x.Series.Shoko.HasValue && x.Episodes.All(e => e.Shoko.HasValue)).Select(x => x.Series.Shoko!.Value).ToHashSet();
                        if (seriesIds.Count is 1) {
                            totalSingleSeriesFiles++;
                            singleSeriesIds.Add(seriesIds.First());
                            foreach (var seriesId in seriesIds)
                                yield return (sourceLocation, file.Id.ToString(), seriesId.ToString());
                        }
                        else if (seriesIds.Count > 1) {
                            multiSeriesFiles.Add((file, sourceLocation));
                        }
                        break;
                    }
                }
            } while (pages.Count > 0);
        }

        // Check which series of the multiple series we have, and only yield
        // the paths for the series we have. This will fail if an OVA episode is
        // linked to both the OVA and e.g. a specials for the TV Series.
        var totalMultiSeriesFiles = 0;
        if (multiSeriesFiles.Count > 0) {
            var mappedSingleSeriesIds = singleSeriesIds
                .SelectMany(seriesId =>
                    ApiManager.GetShowInfosForShokoSeries(seriesId.ToString())
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .GetResult()
                        .Select(showInfo => showInfo.Id)
                )
                .ToHashSet();
            foreach (var (file, sourceLocation) in multiSeriesFiles) {
                var seriesIds = file.CrossReferences
                    .Where(xref => xref.Series.Shoko.HasValue && xref.Episodes.All(e => e.Shoko.HasValue))
                    .Select(xref => (seriesId: xref.Series.Shoko!.Value.ToString(), anidbId: xref.Series.AniDB))
                    .Distinct()
                    .Select(tuple => (
                        tuple.seriesId,
                        tuple.anidbId,
                        showIds: ApiManager.GetShowInfosForShokoSeries(tuple.seriesId).ConfigureAwait(false).GetAwaiter().GetResult().Select(showInfo => showInfo.Id).ToHashSet()
                    ))
                    .Where(tuple => tuple.showIds.Count > 0 && (mappedSingleSeriesIds.Overlaps(tuple.showIds) || AnidbExceptionSet.Contains(tuple.anidbId)))
                    .Select(tuple => tuple.seriesId)
                    .ToList();
                foreach (var seriesId in seriesIds)
                    yield return (sourceLocation, file.Id.ToString(), seriesId);
                totalMultiSeriesFiles += seriesIds.Count;
            }
        }

        var timeSpent = DateTime.UtcNow - start;
        Logger.LogDebug(
            "Iterated {FileCount} ({MultiFileCount}→{MultiFileCount}) files to potentially use within {Count} media folders in {TimeSpan} (Library={LibraryId})",
            totalSingleSeriesFiles,
            multiSeriesFiles.Count,
            totalMultiSeriesFiles,
            mediaConfigs.Count,
            timeSpent,
            libraryId
        );
    }

    private async Task<ListResult<API.Models.File>> GetManagedFolderFilesPage(int managedFolderId, string managedFolderSubPath, int page, SemaphoreSlim semaphore) {
        await semaphore.WaitAsync().ConfigureAwait(false);
        return await ApiClient.GetFilesInManagedFolder(managedFolderId, managedFolderSubPath, page).ConfigureAwait(false);
    }

    private async Task<LinkGenerationResult> GenerateStructure(CollectionType? collectionType, string vfsPath, IEnumerable<(string sourceLocation, string fileId, string seriesId)> allFiles, bool preview = false) {
        var result = new LinkGenerationResult();
        var maxTotalExceptions = Plugin.Instance.Configuration.VFS_MaxTotalExceptionsBeforeAbort;
        var maxSeriesExceptions = Plugin.Instance.Configuration.VFS_MaxSeriesExceptionsBeforeAbort;
        var failedSeries = new HashSet<string>();
        var failedExceptions = new List<Exception>();
        var cancelTokenSource = new CancellationTokenSource();
        var threadCount = Plugin.Instance.Configuration.VFS_Threads is > 0
            ? Plugin.Instance.Configuration.VFS_Threads
            : Plugin.Instance.Configuration.VFS_Threads is -1
                ? ConfigurationManager.Configuration.LibraryScanFanoutConcurrency
                : Environment.ProcessorCount;
        var semaphore = new SemaphoreSlim(threadCount);
        await Task.WhenAll(allFiles.Select(async (tuple) => {
            await semaphore.WaitAsync().ConfigureAwait(false);
            var (sourceLocation, fileId, seriesId) = tuple;

            try {
                if (cancelTokenSource.IsCancellationRequested) {
                    Logger.LogTrace("Cancelling generation of links for {Path}", sourceLocation);
                    return;
                }

                Logger.LogTrace("Generating links for {Path} (File={FileId},Series={SeriesId})", sourceLocation, fileId, seriesId);

                var (symbolicLinks, importedAt) = await GenerateLocationsForFile(collectionType, vfsPath, sourceLocation, fileId, seriesId).ConfigureAwait(false);
                if (symbolicLinks.Length == 0 || !importedAt.HasValue)
                    return;

                var subResult = GenerateSymbolicLinks(sourceLocation, symbolicLinks, importedAt.Value, preview);

                // Combine the current results with the overall results.
                lock (semaphore) {
                    result += subResult;
                }
            }
            catch (Exception ex) {
                Logger.LogWarning(ex, "Failed to generate links for {Path} (File={FileId},Series={SeriesId})", sourceLocation, fileId, seriesId);
                lock (semaphore) {
                    failedSeries.Add(seriesId);
                    failedExceptions.Add(ex);
                    if ((maxSeriesExceptions > 0 && failedSeries.Count == maxSeriesExceptions) ||
                        (maxTotalExceptions > 0 && failedExceptions.Count == maxTotalExceptions)) {
                        cancelTokenSource.Cancel();
                    }
                }
            }
            finally {
                semaphore.Release();
            }
        })).ConfigureAwait(false);

        // Throw an `AggregateException` if any series exceeded the maximum number of exceptions, or if the total number of exceptions exceeded the maximum allowed. Additionally,
        // if no links were generated and there were any exceptions, but we haven't reached the maximum allowed exceptions yet, then also throw an `AggregateException`.
        if (cancelTokenSource.IsCancellationRequested || (failedExceptions.Count > 0 && (maxTotalExceptions > 0 || maxSeriesExceptions > 0) && result.TotalVideos == 0)) {
            Logger.LogWarning("Failed to generate {FileCount} links across {SeriesCount} series for {Path}", failedExceptions.Count, failedSeries.Count, vfsPath);
            throw new AggregateException(failedExceptions);
        }

        return result;
    }

    public async Task<(string[] symbolicLinks, DateTime? importedAt)> GenerateLocationsForFile(CollectionType? collectionType, string vfsPath, string sourceLocation, string fileId, string seriesId) {
        var file = await ApiManager.GetFileInfo(fileId, seriesId).ConfigureAwait(false);
        if (file is null)
            return ([], null);

        if (file.EpisodeList is not { Count: > 0 })
            return ([], null);

        var (episode, episodeXref, _) = file.EpisodeList[0];
        var season = await ApiManager.GetSeasonInfo(episode.SeasonId).ConfigureAwait(false);
        if (season is null)
            return ([], null);

        var isMovieSeason = season.Type is SeriesType.Movie;
        var isMovieLibrary = collectionType is CollectionType.movies || (collectionType is null && isMovieSeason);
        var config = Plugin.Instance.Configuration;
        var shouldAbort = collectionType switch {
            CollectionType.tvshows => isMovieSeason && config.SeparateMovies,
            CollectionType.movies => !isMovieSeason && config.FilterMovieLibraries,
            _ => false,
        };
        if (shouldAbort)
            return ([], null);

        var show = await ApiManager.GetShowInfoBySeasonId(season.Id).ConfigureAwait(false);
        if (show is null)
            return ([], null);

        var showName = (show.Titles.FirstOrDefault(t => t.Source is "AniDB" && t.IsDefault)?.Value ?? show.Titles.FirstOrDefault(t => t.Source is "TMDB" && t.IsDefault)?.Value)?.ReplaceInvalidPathCharacters();
        if (string.IsNullOrWhiteSpace(showName))
            showName = isMovieLibrary ? "Movie" : "Series";
        var episodeNumber = Ordering.GetEpisodeNumber(show, season, episode);
        var episodeName = (episode.Titles.FirstOrDefault(t => t.Source is "AniDB" && t.LanguageCode == "en")?.Value ?? $"{(episode.Type is EpisodeType.Normal ? "Episode " : $"{episode.Type} ")}{episodeNumber}").ReplaceInvalidPathCharacters();

        // For those **really** long names we have to cut if off at some point…
        if (showName.Length >= NameCutOff)
            showName = showName[..NameCutOff].Split(' ').SkipLast(1).Join(' ') + "…";
        if (episodeName.Length >= NameCutOff)
            episodeName = episodeName[..NameCutOff].Split(' ').SkipLast(1).Join(' ') + "…";

        var isExtra = file.EpisodeList.Any(eI => season.IsExtraEpisode(eI.Episode));
        var folders = new List<string>();
        var extrasFolders = file.ExtraType switch {
            null => isExtra ? new string[] { "extras" } : null,
            ExtraType.ThemeSong => ["theme-music"],
            ExtraType.ThemeVideo => config.AddCreditsAsThemeVideos && config.AddCreditsAsSpecialFeatures
                ? ["backdrops", "extras"]
                : config.AddCreditsAsThemeVideos
                ? ["backdrops"]
                : config.AddCreditsAsSpecialFeatures
                ? ["extras"]
                : [],
            ExtraType.Trailer => config.AddTrailers
                ? ["trailers"]
                : [],
            ExtraType.BehindTheScenes => ["behind the scenes"],
            ExtraType.DeletedScene => ["deleted scenes"],
            ExtraType.Clip => ["clips"],
            ExtraType.Interview => ["interviews"],
            ExtraType.Scene => ["scenes"],
            ExtraType.Sample => ["samples"],
            _ => ["extras"],
        };
        var fileIdList = fileId;
        var filePartSuffix = "";
        if (isMovieLibrary) {
            if (extrasFolders != null) {
                foreach (var extrasFolder in extrasFolders)
                    foreach (var episodeInfo in season.EpisodeList.Where(e => e.IsAvailable))
                        folders.Add(Path.Join(vfsPath, $"{showName} [{ProviderNames.ShokoSeries}={show.Id}] [{ProviderNames.ShokoEpisode}={episodeInfo.Id}]", extrasFolder));
            }
            else {
                folders.Add(Path.Join(vfsPath, $"{showName} [{ProviderNames.ShokoSeries}={show.Id}] [{ProviderNames.ShokoEpisode}={episode.Id}]"));
                episodeName = "Movie";
            }
        }
        else {
            var isSpecial = show.IsSpecial(episode);
            var seasonNumber = Ordering.GetSeasonNumber(show, season, episode);
            var seasonFolder = $"Season {(isSpecial ? 0 : seasonNumber).ToString().PadLeft(2, '0')}";
            var showFolder = $"{showName} [{ProviderNames.ShokoSeries}={show.Id}]";
            if (extrasFolders != null) {
                foreach (var extrasFolder in extrasFolders) {
                    folders.Add(Path.Join(vfsPath, showFolder, extrasFolder));

                    // Only place the extra within the season if we have a season number assigned to the episode.
                    if (seasonNumber is not 0)
                        folders.Add(Path.Join(vfsPath, showFolder, seasonFolder, extrasFolder));
                }
            }
            else {
                folders.Add(Path.Join(vfsPath, showFolder, seasonFolder));
                episodeName = $"{showName} S{(isSpecial ? 0 : seasonNumber).ToString().PadLeft(2, '0')}E{episodeNumber.ToString().PadLeft(show.EpisodePadding, '0')}";
                if (episodeXref.Percentage.Group is not 1) {
                    var list = episode.CrossReferences.Where(xref => xref.ReleaseGroup == episodeXref.ReleaseGroup && xref.Percentage.Group == episodeXref.Percentage.Group).ToList();
                    var files = (await Task.WhenAll(list.Select(xref => ApiClient.GetFileByEd2kAndFileSize(xref.ED2K, xref.FileSize))).ConfigureAwait(false))
                        .OfType<API.Models.File>()
                        .ToList();
                    if (files.Count != list.Count)
                        throw new Exception($"Mismatch between cross-references and files. (FileCount={files.Count},CrossReferenceCount={list.Count},Episode={episode.Id},File={fileId},Series={seriesId})");

                    var index = list.FindIndex(xref => xref.Percentage.Start == episodeXref.Percentage.Start && xref.Percentage.End == episodeXref.Percentage.End);
                    filePartSuffix = $".pt{index + 1}";
                    fileIdList = files.Select(f => f.Id.ToString()).Join(",");
                }
            }
        }

        var extraDetails = new List<string>();
        if (config.VFS_AddReleaseGroup)
            extraDetails.Add(
                file.Shoko.Release?.Group is { } releaseGroup
                    ? !string.IsNullOrEmpty(releaseGroup.ShortName)
                        ? releaseGroup.ShortName
                        : !string.IsNullOrEmpty(releaseGroup.Name)
                            ? releaseGroup.Name
                            : $"Release group {releaseGroup.Id}"
                : "No Group"
            );
        if (config.VFS_AddResolution && !string.IsNullOrEmpty(file.Shoko.Resolution))
            extraDetails.Add(file.Shoko.Resolution);
        var fileName = $"{episodeName} {(extraDetails.Count is > 0 ? $"[{extraDetails.Select(a => a.ReplaceInvalidPathCharacters()).Join("] [")}] " : "")}[{ProviderNames.ShokoSeries}={seriesId}] [{ProviderNames.ShokoFile}={fileIdList}]{filePartSuffix}{Path.GetExtension(sourceLocation)}";
        var symbolicLinks = folders
            .Select(folderPath => Path.Join(folderPath, fileName))
            .ToArray();

        foreach (var symbolicLink in symbolicLinks)
            ApiManager.AddFileLookupIds(symbolicLink, fileId, seriesId, file.EpisodeList.Select(episode => episode.Id));
        return (symbolicLinks, (file.Shoko.ImportedAt ?? file.Shoko.CreatedAt).ToLocalTime());
    }

    public LinkGenerationResult GenerateSymbolicLinks(string sourceLocation, string[] symbolicLinks, DateTime importedAt, bool preview = false) {
        try {
            var result = new LinkGenerationResult();
            if (Plugin.Instance.Configuration.VFS_ResolveLinks && !preview) {
                Logger.LogTrace("Attempting to resolve link for {Path}", sourceLocation);
                try {
                    if (File.ResolveLinkTarget(sourceLocation, true) is { } linkTarget) {
                        Logger.LogTrace("Resolved link for {Path} to {LinkTarget}", sourceLocation, linkTarget.FullName);
                        sourceLocation = linkTarget.FullName;
                    }
                }
                catch (Exception ex) {
                    Logger.LogWarning(ex, "Unable to resolve link target for {Path}", sourceLocation);
                    return result;
                }
            }

            var sourcePrefixLength = sourceLocation.Length - Path.GetExtension(sourceLocation).Length;
            var externalFiles = FindExternalFilesForPath(sourceLocation, ExternalSubtitlePathParser)
                .Concat(FindExternalFilesForPath(sourceLocation, ExternalAudioPathParser))
                .ToList();
            foreach (var symbolicLink in symbolicLinks) {
                var symbolicDirectory = Path.GetDirectoryName(symbolicLink)!;
                if (!Directory.Exists(symbolicDirectory))
                    Directory.CreateDirectory(symbolicDirectory);

                result.Paths.Add(symbolicLink);
                if (!File.Exists(symbolicLink)) {
                    result.CreatedVideos++;
                    if (!preview) {
                        Logger.LogDebug("Linking {Link} → {LinkTarget}", symbolicLink, sourceLocation);
                        File.CreateSymbolicLink(symbolicLink, sourceLocation);
                        // Mock the creation date to fake the "date added" order in Jellyfin.
                        File.SetCreationTime(symbolicLink, importedAt);
                    }
                }
                else {
                    var shouldFix = false;
                    try {
                        var nextTarget = File.ResolveLinkTarget(symbolicLink, false);
                        if (!string.Equals(sourceLocation, nextTarget?.FullName)) {
                            shouldFix = true;
                            if (!preview)
                                Logger.LogWarning("Fixing broken symbolic link {Link} → {LinkTarget} (RealTarget={RealTarget})", symbolicLink, sourceLocation, nextTarget?.FullName);
                        }
                        var date = File.GetCreationTime(symbolicLink).ToLocalTime();
                        if (date != importedAt) {
                            shouldFix = true;
                            if (!preview)
                                Logger.LogWarning("Fixing broken symbolic link {Link} with incorrect date.", symbolicLink);
                        }
                    }
                    catch (Exception ex) {
                        shouldFix = true;
                        if (!preview)
                            Logger.LogError(ex, "Encountered an error trying to resolve symbolic link {Link}", symbolicLink);
                    }
                    if (shouldFix) {
                        result.FixedVideos++;
                        if (!preview) {
                            File.Delete(symbolicLink);
                            File.CreateSymbolicLink(symbolicLink, sourceLocation);
                            // Mock the creation date to fake the "date added" order in Jellyfin.
                            File.SetCreationTime(symbolicLink, importedAt);
                        }
                    }
                    else {
                        result.SkippedVideos++;
                    }
                }

                var trickplayLocation = Path.ChangeExtension(sourceLocation, ".trickplay");
                if (Directory.Exists(trickplayLocation)) {
                    var symbolicName = Path.GetFileNameWithoutExtension(symbolicLink);
                    var symbolicTrickplay = Path.Join(symbolicDirectory, symbolicName + ".trickplay");
                    result.Paths.Add(symbolicTrickplay);
                    if (!Directory.Exists(symbolicTrickplay)) {
                        result.CreatedTrickplayDirectories++;
                        if (!preview) {
                            Logger.LogDebug("Linking {Link} → {LinkTarget}", symbolicTrickplay, trickplayLocation);
                            Directory.CreateSymbolicLink(symbolicTrickplay, trickplayLocation);
                        }
                    }
                    else {
                        var shouldFix = false;
                        try {
                            var nextTarget = Directory.ResolveLinkTarget(symbolicTrickplay, false);
                            if (!string.Equals(trickplayLocation, nextTarget?.FullName)) {
                                shouldFix = true;
                                if (!preview)
                                    Logger.LogWarning("Fixing broken symbolic link {Link} → {LinkTarget} (RealTarget={RealTarget})", symbolicTrickplay, trickplayLocation, nextTarget?.FullName);
                            }
                        }
                        catch (Exception ex) {
                            shouldFix = true;
                            if (!preview)
                                Logger.LogError(ex, "Encountered an error trying to resolve symbolic link {Link}", symbolicTrickplay);
                        }
                        if (shouldFix) {
                            result.FixedTrickplayDirectories++;
                            if (!preview) {
                                if ((File.GetAttributes(symbolicTrickplay) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) {
                                    File.Delete(symbolicTrickplay);
                                }
                                else {
                                    if (Directory.GetCreationTime(symbolicTrickplay) > Directory.GetCreationTime(trickplayLocation)) {
                                        Logger.LogTrace("Replacing trickplay for target {Link} → {LinkTarget}", symbolicTrickplay, trickplayLocation);
                                        try {
                                            Directory.Delete(trickplayLocation, recursive: true);
                                            Directory.CreateDirectory(trickplayLocation);
                                            CopyDirectory(symbolicDirectory, trickplayLocation);
                                        }
                                        catch (Exception ex) {
                                            if (!preview)
                                                Logger.LogError(ex, "Failed to replace trickplay for target {Link} → {LinkTarget}", symbolicTrickplay, trickplayLocation);
                                        }
                                    }
                                    Directory.Delete(symbolicTrickplay, recursive: true);
                                }
                                Directory.CreateSymbolicLink(symbolicTrickplay, trickplayLocation);
                            }
                        }
                        else {
                            result.SkippedTrickplayDirectories++;
                        }
                    }
                }

                LinkExternalFiles(externalFiles, symbolicLink, symbolicDirectory, sourcePrefixLength, result, preview);
            }

            return result;
        }
        catch (Exception ex) {
            Logger.LogError(ex, "An error occurred while trying to generate {LinkCount} links for {SourceLocation}; {ErrorMessage}", symbolicLinks.Length, sourceLocation, ex.Message);
            throw;
        }
    }

    private List<string> FindExternalFilesForPath(string sourcePath, ExternalPathParser parser) {
        var externalPaths = new List<string>();
        var folderPath = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrEmpty(folderPath) || !FileSystem.DirectoryExists(folderPath))
            return externalPaths;

        var files = FileSystem.GetFilePaths(folderPath)
            .Except([sourcePath])
            .ToList();
        var sourcePrefix = Path.GetFileNameWithoutExtension(sourcePath);
        foreach (var file in files) {
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);
            if (
                fileNameWithoutExtension.Length >= sourcePrefix.Length &&
                sourcePrefix.Equals(fileNameWithoutExtension[..sourcePrefix.Length], StringComparison.OrdinalIgnoreCase) &&
                (fileNameWithoutExtension.Length == sourcePrefix.Length || NamingOptions.MediaFlagDelimiters.Contains(fileNameWithoutExtension[sourcePrefix.Length]))
            ) {
                var externalPathInfo = parser.ParseFile(file, fileNameWithoutExtension[sourcePrefix.Length..].ToString());
                if (externalPathInfo is not null && !string.IsNullOrEmpty(externalPathInfo.Path))
                    externalPaths.Add(externalPathInfo.Path);
            }
        }

        return externalPaths;
    }

    private void LinkExternalFiles(List<string> externalFiles, string symbolicLink, string symbolicDirectory, int sourcePrefixLength, LinkGenerationResult result, bool preview) {
        if (externalFiles.Count == 0)
            return;

        var symbolicName = Path.GetFileNameWithoutExtension(symbolicLink);
        foreach (var externalSource in externalFiles) {
            var extName = externalSource[sourcePrefixLength..];
            var externalLink = Path.Join(symbolicDirectory, symbolicName + extName);

            result.Paths.Add(externalLink);
            if (!File.Exists(externalLink)) {
                result.CreatedExternalFiles++;
                if (!preview) {
                    Logger.LogDebug("Linking {Link} → {LinkTarget}", externalLink, externalSource);
                    File.CreateSymbolicLink(externalLink, externalSource);
                }
            }
            else {
                var shouldFix = false;
                try {
                    var nextTarget = File.ResolveLinkTarget(externalLink, false);
                    if (!string.Equals(externalSource, nextTarget?.FullName)) {
                        shouldFix = true;
                        if (!preview)
                            Logger.LogWarning("Fixing broken symbolic link {Link} → {LinkTarget} (RealTarget={RealTarget})", externalLink, externalSource, nextTarget?.FullName);
                    }
                }
                catch (Exception ex) {
                    shouldFix = true;
                    if (!preview)
                        Logger.LogError(ex, "Encountered an error trying to resolve symbolic link {Link} for {LinkTarget}", externalLink, externalSource);
                }
                if (shouldFix) {
                    result.FixedExternalFiles++;
                    if (!preview) {
                        File.Delete(externalLink);
                        File.CreateSymbolicLink(externalLink, externalSource);
                    }
                }
                else {
                    result.SkippedExternalFiles++;
                }
            }
        }
    }

    private LinkGenerationResult CleanupStructure(string vfsPath, string directoryToClean, IReadOnlyList<string> allKnownPaths, bool preview = false) {
        if (!FileSystem.DirectoryExists(directoryToClean)) {
            if (!preview)
                Logger.LogDebug("Skipped cleaning up folder because it does not exist: {Path}", directoryToClean);
            return new();
        }

        if (!preview)
            Logger.LogDebug("Looking for file system entries to remove in folder: {Path}", directoryToClean);
        var start = DateTime.Now;
        var previousStep = start;
        var result = new LinkGenerationResult();
        var searchExtensions = NamingOptions.VideoFileExtensions.Concat(NamingOptions.SubtitleFileExtensions).Concat(NamingOptions.AudioFileExtensions).Concat([".nfo", ".trickplay"]).ToHashSet();
        var entriesToBeRemoved = FileSystem.GetFileSystemEntryPaths(directoryToClean, true)
            .Select(path => (path, extName: Path.GetExtension(path)))
            .Where(tuple => !string.IsNullOrEmpty(tuple.extName) && searchExtensions.Contains(tuple.extName))
            .ExceptBy(allKnownPaths, tuple => tuple.path)
            .ToList();

        var nextStep = DateTime.Now;
        if (!preview)
            Logger.LogDebug("Found {FileCount} file system entries to potentially remove or fix in {TimeSpent} in folder: {DirectoryToClean}", entriesToBeRemoved.Count, nextStep - previousStep, directoryToClean);
        previousStep = nextStep;

        foreach (var (location, extName) in entriesToBeRemoved) {
            if (extName is ".nfo") {
                if (!preview) {
                    try {
                        Logger.LogTrace("Removing NFO file at {Path}", location);
                        File.Delete(location);
                    }
                    catch (Exception ex) {
                        Logger.LogError(ex, "Encountered an error trying to remove {FilePath}", location);
                        continue;
                    }
                }
                result.RemovedPaths.Add(location);
                result.RemovedNfos++;
            }
            else if (extName is ".trickplay") {
                if (TryMoveTrickplayDirectory(allKnownPaths, location, preview, out var skip)) {
                    result.Paths.Add(location);
                    if (skip) {
                        result.SkippedTrickplayDirectories++;
                    }
                    else {
                        result.FixedTrickplayDirectories++;
                    }
                    continue;
                }

                if (!preview) {
                    try {
                        Logger.LogTrace("Removing trickplay directory at {Path}", location);
                        if ((File.GetAttributes(location) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) {
                            File.Delete(location);
                        }
                        else {
                            Directory.Delete(location, recursive: true);
                        }
                    }
                    catch (Exception ex) {
                        Logger.LogError(ex, "Encountered an error trying to remove {FilePath}", location);
                        continue;
                    }
                }
                result.RemovedPaths.Add(location);
                result.RemovedTrickplayDirectories++;
            }
            else if (NamingOptions.SubtitleFileExtensions.Contains(extName) || NamingOptions.AudioFileExtensions.Contains(extName)) {
                if (TryMoveExternalFile(allKnownPaths, location, preview, out var skip)) {
                    result.Paths.Add(location);
                    if (skip) {
                        result.SkippedExternalFiles++;
                    }
                    else {
                        result.FixedExternalFiles++;
                    }
                    continue;
                }

                if (!preview) {
                    try {
                        Logger.LogTrace("Removing external file at {Path}", location);
                        File.Delete(location);
                    }
                    catch (Exception ex) {
                        Logger.LogError(ex, "Encountered an error trying to remove {FilePath}", location);
                        continue;
                    }
                }
                result.RemovedPaths.Add(location);
                result.RemovedExternalFiles++;
            }
            else {
                if (ShouldIgnoreVideo(vfsPath, location)) {
                    result.Paths.Add(location);
                    result.SkippedVideos++;
                    continue;
                }

                if (!preview) {
                    try {
                        Logger.LogTrace("Removing video file at {Path}", location);
                        File.Delete(location);
                    }
                    catch (Exception ex) {
                        Logger.LogError(ex, "Encountered an error trying to remove {FilePath}", location);
                        continue;
                    }
                }
                result.RemovedPaths.Add(location);
                result.RemovedVideos++;
            }
        }

        nextStep = DateTime.Now;
        if (!preview) {
            Logger.LogTrace("Removed {FileCount} file system entries in {DirectoryToClean} in {TimeSpent} (Total={TotalSpent})", result.Removed, directoryToClean, nextStep - previousStep, nextStep - start);
        }
        previousStep = nextStep;

        if (preview)
            return result;

        nextStep = DateTime.Now;
        Logger.LogTrace("Removed {FileCount} file system entries in {DirectoryToClean} in {TimeSpent} (Total={TotalSpent})", result.Removed, directoryToClean, nextStep - previousStep, nextStep - start);
        previousStep = nextStep;

        var cleaned = 0;
        var directoriesToClean = entriesToBeRemoved
            .SelectMany(tuple => {
                var path = Path.GetDirectoryName(tuple.path);
                var paths = new List<(string path, int level)>();
                while (!string.IsNullOrEmpty(path)) {
                    var level = path == directoryToClean ? 0 : path[(directoryToClean.Length + 1)..].Split(Path.DirectorySeparatorChar).Length;
                    paths.Add((path, level));
                    if (path == directoryToClean)
                        break;
                    path = Path.GetDirectoryName(path);
                }
                return paths;
            })
            .DistinctBy(tuple => tuple.path)
            .OrderByDescending(tuple => tuple.level)
            .ThenBy(tuple => tuple.path)
            .Select(tuple => tuple.path)
            .ToList();

        nextStep = DateTime.Now;
        Logger.LogDebug("Found {DirectoryCount} directories to potentially clean in {DirectoryToClean} in {TimeSpent} (Total={TotalSpent})", directoriesToClean.Count, directoryToClean, nextStep - previousStep, nextStep - start);
        previousStep = nextStep;

        foreach (var directoryPath in directoriesToClean) {
            if (Directory.Exists(directoryPath) && !Directory.EnumerateFileSystemEntries(directoryPath).Any()) {
                Logger.LogTrace("Removing empty directory at {Path}", directoryPath);
                Directory.Delete(directoryPath);
                cleaned++;
            }
        }

        Logger.LogTrace("Cleaned {CleanedCount} directories in {DirectoryToClean} in {TimeSpent} (Total={TotalSpent})", cleaned, directoryToClean, nextStep - previousStep, nextStep - start);

        return result;
    }

    private bool TryMoveExternalFile(IReadOnlyList<string> allKnownPaths, string externalFilePath, bool preview, out bool skip) {
        if (!TryGetIdsForPath(externalFilePath, out var fileId, out var seriesId)){
            skip = false;
            return false;
        }

        var symbolicLink = allKnownPaths.FirstOrDefault(knownPath => NamingOptions.VideoFileExtensions.Contains(Path.GetExtension(knownPath)) && TryGetIdsForPath(knownPath, out var knownFileId, out var knownSeriesId) && seriesId == knownSeriesId && fileId == knownFileId);
        if (string.IsNullOrEmpty(symbolicLink)){
            skip = false;
            return false;
        }

        var sourcePathWithoutExt = symbolicLink[..^Path.GetExtension(symbolicLink).Length];
        if (!externalFilePath.StartsWith(sourcePathWithoutExt)){
            skip = false;
            return false;
        }

        var extName = externalFilePath[sourcePathWithoutExt.Length..];
        string? realTarget = null;
        try {
            realTarget = File.ResolveLinkTarget(symbolicLink, false)?.FullName;
        }
        catch { }
        if (string.IsNullOrEmpty(realTarget)){
            skip = false;
            return false;
        }

        if (preview){
            skip = true;
            return true;
        }

        try {
            var currentTarget = File.ResolveLinkTarget(externalFilePath, false)?.FullName;
            if (!string.IsNullOrEmpty(currentTarget)) {
                // Just remove the link if the target doesn't exist.
                if (!File.Exists(currentTarget)){
                    skip = false;
                    return false;
                }

                // // This statement will never be true. Because it would never had hit this path if it were true.
                // if (currentTarget == realTarget)
                //     return true;

                // Copy the link so we can move it to where it should be.
                File.Delete(externalFilePath);
                File.Copy(currentTarget, externalFilePath);
            }
        }
        catch (Exception ex) {
            Logger.LogWarning(ex, "Unable to check if {Path} is a symbolic link", externalFilePath);
            skip = false;
            return false;
        }

        var realExternalFilePath = realTarget[..^Path.GetExtension(realTarget).Length] + extName;
        if (!File.Exists(realExternalFilePath)) {
            try {
                File.Move(externalFilePath, realExternalFilePath);
            }
            catch (Exception) {
                Logger.LogWarning("Skipped moving {Path} to {RealPath} because we don't have permissions.", externalFilePath, realExternalFilePath);
                skip = true;
                return true;
            }
        }
        else {
            File.Delete(externalFilePath);
        }

        File.CreateSymbolicLink(externalFilePath, realExternalFilePath);
        Logger.LogDebug("Moved {Path} to {RealPath}", externalFilePath, realExternalFilePath);

        skip = false;
        return true;
    }

    private bool TryMoveTrickplayDirectory(IReadOnlyList<string> allKnownPaths, string trickplayDirectory, bool preview, out bool skip) {
        if (!TryGetIdsForPath(trickplayDirectory, out var fileId, out var seriesId)) {
            skip = false;
            return false;
        }

        var linkToMove = allKnownPaths.FirstOrDefault(knownPath => NamingOptions.VideoFileExtensions.Contains(Path.GetExtension(knownPath)) && TryGetIdsForPath(knownPath, out var knownFileId, out var knownSeriesId) && seriesId == knownSeriesId && fileId == knownFileId);
        if (string.IsNullOrEmpty(linkToMove)){
            skip = false;
            return false;
        }

        var sourcePathWithoutExt = linkToMove[..^Path.GetExtension(linkToMove).Length];
        if (!trickplayDirectory.StartsWith(sourcePathWithoutExt)){
            skip = false;
            return false;
        }

        var extName = trickplayDirectory[sourcePathWithoutExt.Length..];
        string? realTarget = null;
        try {
            realTarget = Directory.ResolveLinkTarget(linkToMove, false)?.FullName;
        }
        catch { }
        if (string.IsNullOrEmpty(realTarget)){
            skip = false;
            return false;
        }

        if (preview){
            skip = true;
            return true;
        }

        try {
            var currentTarget = Directory.ResolveLinkTarget(trickplayDirectory, false)?.FullName;
            if (!string.IsNullOrEmpty(currentTarget)) {
                // Just remove the link if the target doesn't exist.
                if (!Directory.Exists(currentTarget)){
                    skip = false;
                    return false;
                }

                // // This statement will never be true. Because it would never had hit this path if it were true.
                // if (currentTarget == realTarget)
                //     return true;

                // Copy the link so we can move it to where it should be.
                Directory.Delete(trickplayDirectory, recursive: true);
                CopyDirectory(currentTarget, trickplayDirectory);
            }
        }
        catch (Exception ex) {
            Logger.LogWarning(ex, "Unable to check if {Path} is a symbolic link", trickplayDirectory);
            skip = false;
            return false;
        }

        var realPath = realTarget[..^Path.GetExtension(realTarget).Length] + extName;
        if (!FileSystem.DirectoryExists(realPath)) {
            try {
                Directory.Move(trickplayDirectory, realPath);
            }
            catch (Exception) {
                try {
                    Directory.CreateDirectory(realPath);
                }
                catch (Exception) {
                    Logger.LogDebug("Skipped moving {Directory} to {RealPath} because we don't have permissions.", trickplayDirectory, realPath);
                    skip = true;
                    return true;
                }
                CopyDirectory(trickplayDirectory, realPath);
                Directory.Delete(trickplayDirectory, recursive: true);
            }
        }
        else {
            Directory.Delete(trickplayDirectory, recursive: true);
        }
        Directory.CreateSymbolicLink(trickplayDirectory, realPath);
        Logger.LogDebug("Moved {Path} to {RealPath}", trickplayDirectory, realPath);

        skip = false;
        return true;
    }

    private void CopyDirectory(string source, string destination) {
        if (!Directory.Exists(destination))
            Directory.CreateDirectory(destination);

        foreach (var file in FileSystem.GetFilePaths(source, true)) {
            var newFile = Path.Combine(destination, file[(source.Length + 1)..]);
            var directoryOfFile = Path.GetDirectoryName(newFile)!;
            if (!FileSystem.DirectoryExists(directoryOfFile))
                Directory.CreateDirectory(directoryOfFile);
            File.Copy(file, newFile, true);
        }
    }

    private static bool ShouldIgnoreVideo(string vfsPath, string path) {
        // Ignore the video if it's within one of the folders to potentially ignore _and_ it doesn't have any shoko ids set.
        var parentDirectories = path[(vfsPath.Length + 1)..].Split(Path.DirectorySeparatorChar).SkipLast(1).ToArray();
        return parentDirectories.Length > 1 && IgnoreFolderNames.Contains(parentDirectories.Last()) && !TryGetIdsForPath(path, out _, out _);
    }

    public static bool TryGetIdsForPath(string path, [NotNullWhen(true)] out string? fileId, [NotNullWhen(true)] out string? seriesId) {
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (!fileName.TryGetAttributeValue(ProviderNames.ShokoFile, out fileId) || !int.TryParse(fileId, out _) ||
            !fileName.TryGetAttributeValue(ProviderNames.ShokoSeries, out seriesId) || !int.TryParse(seriesId, out _)) {
            seriesId = null;
            fileId = null;
            return false;
        }

        return true;
    }

    #endregion
}
