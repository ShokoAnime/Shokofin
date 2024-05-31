using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Naming.Common;
using Emby.Naming.ExternalFiles;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Models;
using Shokofin.Configuration;
using Shokofin.ExternalIds;
using Shokofin.Resolvers.Models;
using Shokofin.Utils;

using File = System.IO.File;

namespace Shokofin.Resolvers;

public class VirtualFileSystemService
{
    private readonly ShokoAPIManager ApiManager;

    private readonly ShokoAPIClient ApiClient;

    private readonly ILibraryManager LibraryManager;

    private readonly IFileSystem FileSystem;

    private readonly ILogger<VirtualFileSystemService> Logger;

    private readonly MediaFolderConfigurationService ConfigurationService;

    private readonly NamingOptions NamingOptions;

    private readonly ExternalPathParser ExternalPathParser;

    private readonly GuardedMemoryCache DataCache;

    // Note: Out of the 14k entries in my test shoko database, then only **319** entries have a title longer than 100 characters.
    private const int NameCutOff = 64;

    private static readonly HashSet<string> IgnoreFolderNames = new() {
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
    };

    public bool IsCacheStalled => DataCache.IsStalled;

    public VirtualFileSystemService(
        ShokoAPIManager apiManager,
        ShokoAPIClient apiClient,
        MediaFolderConfigurationService configurationService,
        ILibraryManager libraryManager,
        IFileSystem fileSystem,
        ILogger<VirtualFileSystemService> logger,
        ILocalizationManager localizationManager,
        NamingOptions namingOptions
    )
    {
        ApiManager = apiManager;
        ApiClient = apiClient;
        ConfigurationService = configurationService;
        LibraryManager = libraryManager;
        FileSystem = fileSystem;
        Logger = logger;
        DataCache = new(logger, TimeSpan.FromMinutes(15), new() { ExpirationScanFrequency = TimeSpan.FromMinutes(25) }, new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1), SlidingExpiration = TimeSpan.FromMinutes(15) });
        NamingOptions = namingOptions;
        ExternalPathParser = new ExternalPathParser(namingOptions, localizationManager, MediaBrowser.Model.Dlna.DlnaProfileType.Subtitle);
        LibraryManager.ItemRemoved += OnLibraryManagerItemRemoved;
    }

    ~VirtualFileSystemService()
    {
        LibraryManager.ItemRemoved -= OnLibraryManagerItemRemoved;
        DataCache.Dispose();
    }

    public void Clear()
    {
        Logger.LogDebug("Clearing data…");
        DataCache.Clear();
    }

    #region Changes Tracking

    private void OnLibraryManagerItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        // Remove the VFS directory for any media library folders when they're removed.
        var root = LibraryManager.RootFolder;
        if (e.Item != null && root != null && e.Item != root && e.Item is Folder folder && folder.ParentId == Guid.Empty  && !string.IsNullOrEmpty(folder.Path) && !folder.Path.StartsWith(root.Path)) {
            DataCache.Remove($"paths-for-media-folder:{folder.Path}");
            DataCache.Remove($"should-skip-media-folder:{folder.Path}");
            var vfsPath = folder.GetVirtualRoot();
            if (Directory.Exists(vfsPath)) {
                Logger.LogInformation("Removing VFS directory for folder at {Path}", folder.Path);
                Directory.Delete(vfsPath, true);
                Logger.LogInformation("Removed VFS directory for folder at {Path}", folder.Path);
            }
        }
    }

    #endregion

    #region Generate Structure

    /// <summary>
    /// Generates the VFS structure if the VFS is enabled for the <paramref name="mediaFolder"/>.
    /// </summary>
    /// <param name="mediaFolder">The media folder to generate a structure for.</param>
    /// <param name="path">The file or folder within the media folder to generate a structure for.</param>
    /// <returns>The VFS path, if it succeeded.</returns>
    public async Task<string?> GenerateStructureInVFS(Folder mediaFolder, string path)
    {
        // Skip link generation if we've already generated for the media folder.
        var vfsPath = mediaFolder.GetVirtualRoot();
        if (DataCache.TryGetValue<bool>($"should-skip-media-folder:{mediaFolder.Path}", out var shouldReturnPath))
            return shouldReturnPath ? vfsPath : null;

        // Check full path and all parent directories if they have been indexed.
        if (path.StartsWith(vfsPath + Path.DirectorySeparatorChar)) {
            var pathSegments = path[(vfsPath.Length + 1)..].Split(Path.DirectorySeparatorChar).Prepend(vfsPath).ToArray();
            while (pathSegments.Length > 1) {
                var subPath = Path.Join(pathSegments);
                if (DataCache.TryGetValue<bool>($"should-skip-vfs-path:{subPath}", out _))
                    return vfsPath;
                pathSegments = pathSegments.SkipLast(1).ToArray();
            }
        }

        // Only do this once.
        var key = path.StartsWith(mediaFolder.Path)
            ? $"should-skip-media-folder:{mediaFolder.Path}"
            : $"should-skip-vfs-path:{path}";
        shouldReturnPath = await DataCache.GetOrCreateAsync<bool>(key, async (__) => {
            var mediaConfig = await ConfigurationService.GetOrCreateConfigurationForMediaFolder(mediaFolder);
            if (!mediaConfig.IsMapped)
                return false;

            // Return early if we're not going to generate them.
            if (!mediaConfig.IsVirtualFileSystemEnabled)
                return false;

            if (!Plugin.Instance.CanCreateSymbolicLinks)
                throw new Exception("Windows users are required to enable Developer Mode then restart Jellyfin to be able to create symbolic links, a feature required to use the VFS.");

            // Iterate the files already in the VFS.
            string? pathToClean = null;
            IEnumerable<(string sourceLocation, string fileId, string seriesId)>? allFiles = null;
            if (path.StartsWith(vfsPath + Path.DirectorySeparatorChar)) {
                var allPaths = GetPathsForMediaFolder(mediaFolder);
                var pathSegments = path[(vfsPath.Length + 1)..].Split(Path.DirectorySeparatorChar);
                switch (pathSegments.Length) {
                    // show/movie-folder level
                    case 1: {
                        var seriesName = pathSegments[0];
                        if (!seriesName.TryGetAttributeValue(ShokoSeriesId.Name, out var seriesId) || !int.TryParse(seriesId, out _))
                            break;

                        // movie-folder
                        if (seriesName.TryGetAttributeValue(ShokoEpisodeId.Name, out var episodeId) ) {
                            if (!int.TryParse(episodeId, out _))
                                break;

                            pathToClean = path;
                            allFiles = GetFilesForMovie(episodeId, seriesId, mediaConfig.ImportFolderId, mediaConfig.ImportFolderRelativePath, mediaFolder.Path, allPaths);
                            break;
                        }

                        // show
                        pathToClean = path;
                        allFiles = GetFilesForShow(seriesId, null, mediaConfig.ImportFolderId, mediaConfig.ImportFolderRelativePath, mediaFolder.Path, allPaths);
                        break;
                    }

                    // season/movie level
                    case 2: {
                        var (seriesName, seasonOrMovieName) = pathSegments;
                        if (!seriesName.TryGetAttributeValue(ShokoSeriesId.Name, out var seriesId) || !int.TryParse(seriesId, out _))
                            break;

                        // movie
                        if (seriesName.TryGetAttributeValue(ShokoEpisodeId.Name, out _)) {
                            if (!seasonOrMovieName.TryGetAttributeValue(ShokoSeriesId.Name, out seriesId) || !int.TryParse(seriesId, out _))
                                break;

                            if (!seasonOrMovieName.TryGetAttributeValue(ShokoFileId.Name, out var fileId) || !int.TryParse(fileId, out _))
                                break;

                            allFiles = GetFilesForEpisode(fileId, seriesId, mediaConfig.ImportFolderId, mediaConfig.ImportFolderRelativePath, mediaFolder.Path);
                            break;
                        }

                        // "season" or extras
                        if (!seasonOrMovieName.StartsWith("Season ") || !int.TryParse(seasonOrMovieName.Split(' ').Last(), out var seasonNumber))
                            break;

                        pathToClean = path;
                        allFiles = GetFilesForShow(seriesId, seasonNumber, mediaConfig.ImportFolderId, mediaConfig.ImportFolderRelativePath, mediaFolder.Path, allPaths);
                        break;
                    }

                    // episodes level
                    case 3: {
                        var (seriesName, seasonName, episodeName) = pathSegments;
                        if (!seriesName.TryGetAttributeValue(ShokoSeriesId.Name, out var seriesId) || !int.TryParse(seriesId, out _))
                            break;

                        if (!seasonName.StartsWith("Season ") || !int.TryParse(seasonName.Split(' ').Last(), out _))
                            break;

                        if (!episodeName.TryGetAttributeValue(ShokoSeriesId.Name, out seriesId) || !int.TryParse(seriesId, out _))
                            break;

                        if (!episodeName.TryGetAttributeValue(ShokoFileId.Name, out var fileId) || !int.TryParse(fileId, out _))
                            break;

                        allFiles = GetFilesForEpisode(fileId, seriesId, mediaConfig.ImportFolderId, mediaConfig.ImportFolderRelativePath, mediaFolder.Path);
                        break;
                    }
                }
            }
            // Iterate files in the "real" media folder.
            else if (path.StartsWith(mediaFolder.Path)) {
                var allPaths = GetPathsForMediaFolder(mediaFolder);
                pathToClean = vfsPath;
                allFiles = GetFilesForImportFolder(mediaConfig.ImportFolderId, mediaConfig.ImportFolderRelativePath, mediaFolder.Path, allPaths);
            }

            if (allFiles is null)
                return false;

            // Generate and cleanup the structure in the VFS.
            var result = await GenerateStructure(mediaFolder, vfsPath, allFiles);
            if (!string.IsNullOrEmpty(pathToClean))
                result += CleanupStructure(vfsPath, pathToClean, result.Paths.ToArray());

            // Save which paths we've already generated so we can skip generation
            // for them and their sub-paths later, and also print the result.
            result.Print(Logger, path.StartsWith(mediaFolder.Path) ? mediaFolder.Path : path);

            return true;
        });

        return shouldReturnPath ? vfsPath : null;
    }

    private HashSet<string> GetPathsForMediaFolder(Folder mediaFolder)
    {
        Logger.LogDebug("Looking for files in folder at {Path}", mediaFolder.Path);
        var start = DateTime.UtcNow;
        var paths = FileSystem.GetFilePaths(mediaFolder.Path, true)
            .Where(path => NamingOptions.VideoFileExtensions.Contains(Path.GetExtension(path)))
            .ToHashSet();
        Logger.LogDebug("Found {FileCount} files in folder at {Path} in {TimeSpan}.", paths.Count, mediaFolder.Path, DateTime.UtcNow - start);
        return paths;
    }

    private IEnumerable<(string sourceLocation, string fileId, string seriesId)> GetFilesForEpisode(string fileId, string seriesId, int importFolderId, string importFolderSubPath, string mediaFolderPath)
    {
        var start = DateTime.UtcNow;
        var file = ApiClient.GetFile(fileId).ConfigureAwait(false).GetAwaiter().GetResult();
        if (file is null || !file.CrossReferences.Any(xref => xref.Series.ToString() == seriesId))
            yield break;
        Logger.LogDebug(
            "Iterating 1 file to potentially use within media folder at {Path} (File={FileId},Series={SeriesId},ImportFolder={FolderId},RelativePath={RelativePath})",
            mediaFolderPath,
            fileId,
            seriesId,
            importFolderId,
            importFolderSubPath
        );

        var location = file.Locations
            .Where(location => location.ImportFolderId == importFolderId && (importFolderSubPath.Length is 0 || location.RelativePath.StartsWith(importFolderSubPath)))
            .FirstOrDefault();
        if (location is null || file.CrossReferences.Count is 0)
            yield break;

        var sourceLocation = Path.Join(mediaFolderPath, location.RelativePath[importFolderSubPath.Length..]);
        if (!File.Exists(sourceLocation))
            yield break;

        yield return (sourceLocation, fileId, seriesId);

        var timeSpent = DateTime.UtcNow - start;
        Logger.LogDebug(
            "Iterated 1 file to potentially use within media folder at {Path} in {TimeSpan} (File={FileId},Series={SeriesId},ImportFolder={FolderId},RelativePath={RelativePath})",
            mediaFolderPath,
            timeSpent,
            fileId,
            seriesId,
            importFolderId,
            importFolderSubPath
        );
    }

    private IEnumerable<(string sourceLocation, string fileId, string seriesId)> GetFilesForMovie(string episodeId, string seriesId, int importFolderId, string importFolderSubPath, string mediaFolderPath, IReadOnlySet<string> fileSet)
    {
        var start = DateTime.UtcNow;
        var totalFiles = 0;
        var seasonInfo = ApiManager.GetSeasonInfoForSeries(seriesId).ConfigureAwait(false).GetAwaiter().GetResult();
        if (seasonInfo is null)
            yield break;
        Logger.LogDebug(
            "Iterating files to potentially use within media folder at {Path} (Episode={EpisodeId},Series={SeriesId},ImportFolder={FolderId},RelativePath={RelativePath})",
            mediaFolderPath,
            episodeId,
            seriesId,
            importFolderId,
            importFolderSubPath
        );

        var episodeIds = seasonInfo.ExtrasList.Select(episode => episode.Id).Append(episodeId).ToHashSet();
        var files = ApiClient.GetFilesForSeries(seasonInfo.Id).ConfigureAwait(false).GetAwaiter().GetResult();
        var fileLocations = files
            .Where(files => files.CrossReferences.Any(xref => episodeIds.Overlaps(xref.Episodes.Where(e => e.Shoko.HasValue).Select(e => e.Shoko!.Value.ToString()))))
            .SelectMany(file => file.Locations.Select(location => (file, location)))
            .ToList();
        foreach (var (file, location) in fileLocations) {
            if (location.ImportFolderId != importFolderId || importFolderSubPath.Length != 0 && !location.RelativePath.StartsWith(importFolderSubPath))
                continue;

            var sourceLocation = Path.Join(mediaFolderPath, location.RelativePath[importFolderSubPath.Length..]);
            if (!fileSet.Contains(sourceLocation))
                continue;

            totalFiles++;
            yield return (sourceLocation, file.Id.ToString(), seasonInfo.Id);
        }
        var timeSpent = DateTime.UtcNow - start;
        Logger.LogDebug(
            "Iterated {FileCount} file to potentially use within media folder at {Path} in {TimeSpan} (Episode={EpisodeId},Series={SeriesId},ImportFolder={FolderId},RelativePath={RelativePath})",
            totalFiles,
            mediaFolderPath,
            timeSpent,
            episodeId,
            seriesId,
            importFolderId,
            importFolderSubPath
        );
    }

    private IEnumerable<(string sourceLocation, string fileId, string seriesId)> GetFilesForShow(string seriesId, int? seasonNumber, int importFolderId, string importFolderSubPath, string mediaFolderPath, HashSet<string> fileSet)
    {
        var start = DateTime.UtcNow;
        var showInfo = ApiManager.GetShowInfoForSeries(seriesId).ConfigureAwait(false).GetAwaiter().GetResult();
        if (showInfo is null)
            yield break;
        Logger.LogDebug(
            "Iterating files to potentially use within media folder at {Path} (Series={SeriesId},Season={SeasonNumber},ImportFolder={FolderId},RelativePath={RelativePath})",
            mediaFolderPath,
            seriesId,
            seasonNumber,
            importFolderId,
            importFolderSubPath
        );

        // Only return the files for the given season.
        var totalFiles = 0;
        if (seasonNumber.HasValue) {
            // Special handling of specials (pun intended)
            if (seasonNumber.Value is 0) {
                foreach (var seasonInfo in showInfo.SeasonList) {
                    var episodeIds = seasonInfo.SpecialsList.Select(episode => episode.Id).ToHashSet();
                    var files = ApiClient.GetFilesForSeries(seasonInfo.Id).ConfigureAwait(false).GetAwaiter().GetResult();
                    var fileLocations = files
                        .Where(files => files.CrossReferences.Any(xref => episodeIds.Overlaps(xref.Episodes.Where(e => e.Shoko.HasValue).Select(e => e.Shoko!.Value.ToString()))))
                        .SelectMany(file => file.Locations.Select(location => (file, location)))
                        .ToList();
                    foreach (var (file, location) in fileLocations) {
                        if (location.ImportFolderId != importFolderId || importFolderSubPath.Length != 0 && !location.RelativePath.StartsWith(importFolderSubPath))
                            continue;

                        var sourceLocation = Path.Join(mediaFolderPath, location.RelativePath[importFolderSubPath.Length..]);
                        if (!fileSet.Contains(sourceLocation))
                            continue;

                        totalFiles++;
                        yield return (sourceLocation, file.Id.ToString(), seasonInfo.Id);
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
                    var files = ApiClient.GetFilesForSeries(seasonInfo.Id).ConfigureAwait(false).GetAwaiter().GetResult();
                    var fileLocations = files
                        .Where(files => files.CrossReferences.Any(xref => episodeIds.Overlaps(xref.Episodes.Where(e => e.Shoko.HasValue).Select(e => e.Shoko!.Value.ToString()))))
                        .SelectMany(file => file.Locations.Select(location => (file, location)))
                        .ToList();
                    foreach (var (file, location) in fileLocations) {
                        if (location.ImportFolderId != importFolderId || importFolderSubPath.Length != 0 && !location.RelativePath.StartsWith(importFolderSubPath))
                            continue;

                        var sourceLocation = Path.Join(mediaFolderPath, location.RelativePath[importFolderSubPath.Length..]);
                        if (!fileSet.Contains(sourceLocation))
                            continue;

                        totalFiles++;
                        yield return (sourceLocation, file.Id.ToString(), seasonInfo.Id);
                    }
                }
            }
        }
        // Return all files for the show.
        else {
            foreach (var seasonInfo in showInfo.SeasonList) {
                var files = ApiClient.GetFilesForSeries(seasonInfo.Id).ConfigureAwait(false).GetAwaiter().GetResult();
                var fileLocations = files
                    .SelectMany(file => file.Locations.Select(location => (file, location)))
                    .ToList();
                foreach (var (file, location) in fileLocations) {
                    if (location.ImportFolderId != importFolderId || importFolderSubPath.Length != 0 && !location.RelativePath.StartsWith(importFolderSubPath))
                        continue;

                    var sourceLocation = Path.Join(mediaFolderPath, location.RelativePath[importFolderSubPath.Length..]);
                    if (!fileSet.Contains(sourceLocation))
                        continue;

                    totalFiles++;
                    yield return (sourceLocation, file.Id.ToString(), seasonInfo.Id);
                }
            }
        }

        var timeSpent = DateTime.UtcNow - start;
        Logger.LogDebug(
            "Iterated {FileCount} files to potentially use within media folder at {Path} in {TimeSpan} (Series={SeriesId},Season={SeasonNumber},ImportFolder={FolderId},RelativePath={RelativePath})",
            totalFiles,
            mediaFolderPath,
            timeSpent,
            seriesId,
            seasonNumber,
            importFolderId,
            importFolderSubPath
        );
    }

    private IEnumerable<(string sourceLocation, string fileId, string seriesId)> GetFilesForImportFolder(int importFolderId, string importFolderSubPath, string mediaFolderPath, HashSet<string> fileSet)
    {
        var start = DateTime.UtcNow;
        var firstPage = ApiClient.GetFilesForImportFolder(importFolderId, importFolderSubPath);
        var pageData = firstPage
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
        var totalPages = pageData.List.Count == pageData.Total ? 1 : (int)Math.Ceiling((float)pageData.Total / pageData.List.Count);
        Logger.LogDebug(
            "Iterating ≤{FileCount} files to potentially use within media folder at {Path} by checking {TotalCount} matches. (ImportFolder={FolderId},RelativePath={RelativePath},PageSize={PageSize},TotalPages={TotalPages})",
            fileSet.Count,
            mediaFolderPath,
            pageData.Total,
            importFolderId,
            importFolderSubPath,
            pageData.List.Count == pageData.Total ? null : pageData.List.Count,
            totalPages
        );

        // Ensure at most 5 pages are in-flight at any given time, until we're done fetching the pages.
        var semaphore = new SemaphoreSlim(5);
        var pages = new List<Task<ListResult<API.Models.File>>>() { firstPage };
        for (var page = 2; page <= totalPages; page++)
            pages.Add(GetImportFolderFilesPage(importFolderId, importFolderSubPath, page, semaphore));

        var singleSeriesIds = new HashSet<int>();
        var multiSeriesFiles = new List<(API.Models.File, string)>();
        var totalSingleSeriesFiles = 0;
        do {
            var task = Task.WhenAny(pages).ConfigureAwait(false).GetAwaiter().GetResult();
            pages.Remove(task);
            semaphore.Release();
            pageData = task.Result;

            Logger.LogTrace(
                "Iterating page {PageNumber} with size {PageSize} (ImportFolder={FolderId},RelativePath={RelativePath})",
                totalPages - pages.Count,
                pageData.List.Count,
                importFolderId,
                importFolderSubPath
            );
            foreach (var file in pageData.List) {
                if (file.CrossReferences.Count is 0)
                    continue;

                var location = file.Locations
                    .Where(location => location.ImportFolderId == importFolderId && (importFolderSubPath.Length is 0 || location.RelativePath.StartsWith(importFolderSubPath)))
                    .FirstOrDefault();
                if (location is null)
                    continue;

                var sourceLocation = Path.Join(mediaFolderPath, location.RelativePath[importFolderSubPath.Length..]);
                if (!fileSet.Contains(sourceLocation))
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
            }
        } while (pages.Count > 0);

        // Check which series of the multiple series we have, and only yield
        // the paths for the series we have. This will fail if an OVA episode is
        // linked to both the OVA and e.g. a specials for the TV Series.
        var totalMultiSeriesFiles = 0;
        if (multiSeriesFiles.Count > 0) {
            var mappedSingleSeriesIds = singleSeriesIds
                .Select(seriesId =>
                    ApiManager.GetShowInfoForSeries(seriesId.ToString())
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .GetResult()?.Id
                )
                .OfType<string>()
                .ToHashSet();
            foreach (var (file, sourceLocation) in multiSeriesFiles) {
                var seriesIds = file.CrossReferences
                    .Where(xref => xref.Series.Shoko.HasValue && xref.Episodes.All(e => e.Shoko.HasValue))
                    .Select(xref => xref.Series.Shoko!.Value.ToString())
                    .Distinct()
                    .Select(seriesId => (
                        seriesId,
                        showId: ApiManager.GetShowInfoForSeries(seriesId).ConfigureAwait(false).GetAwaiter().GetResult()?.Id
                    ))
                    .Where(tuple => !string.IsNullOrEmpty(tuple.showId) && mappedSingleSeriesIds.Contains(tuple.showId))
                    .Select(tuple => tuple.seriesId)
                    .ToList();
                foreach (var seriesId in seriesIds)
                    yield return (sourceLocation, file.Id.ToString(), seriesId);
                totalMultiSeriesFiles += seriesIds.Count;
            }
        }

        var timeSpent = DateTime.UtcNow - start;
        Logger.LogDebug(
            "Iterated {FileCount} ({MultiFileCount}→{MultiFileCount}) files to potentially use within media folder at {Path} in {TimeSpan} (ImportFolder={FolderId},RelativePath={RelativePath})",
            totalSingleSeriesFiles,
            multiSeriesFiles.Count,
            totalMultiSeriesFiles,
            mediaFolderPath,
            timeSpent,
            importFolderId,
            importFolderSubPath
        );
    }

    private async Task<ListResult<API.Models.File>> GetImportFolderFilesPage(int importFolderId, string importFolderSubPath, int page, SemaphoreSlim semaphore)
    {
        await semaphore.WaitAsync().ConfigureAwait(false);
        return await ApiClient.GetFilesForImportFolder(importFolderId, importFolderSubPath, page).ConfigureAwait(false);
    }

    private async Task<LinkGenerationResult> GenerateStructure(Folder mediaFolder, string vfsPath, IEnumerable<(string sourceLocation, string fileId, string seriesId)> allFiles)
    {
        var result = new LinkGenerationResult();
        var collectionType = LibraryManager.GetInheritedContentType(mediaFolder);
        var semaphore = new SemaphoreSlim(Plugin.Instance.Configuration.VirtualFileSystemThreads);
        await Task.WhenAll(allFiles.Select(async (tuple) => {
            await semaphore.WaitAsync().ConfigureAwait(false);

            try {
                Logger.LogTrace("Generating links for {Path} (File={FileId},Series={SeriesId})", tuple.sourceLocation, tuple.fileId, tuple.seriesId);

                var (sourceLocation, symbolicLinks, importedAt) = await GenerateLocationsForFile(vfsPath, collectionType, tuple.sourceLocation, tuple.fileId, tuple.seriesId).ConfigureAwait(false);

                // Skip any source files we weren't meant to have in the library.
                if (string.IsNullOrEmpty(sourceLocation) || !importedAt.HasValue)
                    return;

                var subResult = GenerateSymbolicLinks(sourceLocation, symbolicLinks, importedAt.Value);

                // Combine the current results with the overall results.
                lock (semaphore) {
                    result += subResult;
                }
            }
            finally {
                semaphore.Release();
            }
        }))
            .ConfigureAwait(false);

        return result;
    }

    public async Task<(string sourceLocation, string[] symbolicLinks, DateTime? importedAt)> GenerateLocationsForFile(Folder mediaFolder, string sourceLocation, string fileId, string seriesId)
    {
        var vfsPath = mediaFolder.GetVirtualRoot();
        var collectionType = LibraryManager.GetInheritedContentType(mediaFolder);
        return await GenerateLocationsForFile(vfsPath, collectionType, sourceLocation, fileId, seriesId);
    }

    private async Task<(string sourceLocation, string[] symbolicLinks, DateTime? importedAt)> GenerateLocationsForFile(string vfsPath, string? collectionType, string sourceLocation, string fileId, string seriesId)
    {
        var season = await ApiManager.GetSeasonInfoForSeries(seriesId).ConfigureAwait(false);
        if (season is null)
            return (string.Empty, Array.Empty<string>(), null);

        var isMovieSeason = season.Type is SeriesType.Movie;
        var config = Plugin.Instance.Configuration;
        var shouldAbort = collectionType switch {
            CollectionType.TvShows => isMovieSeason && config.SeparateMovies,
            CollectionType.Movies => !isMovieSeason,
            _ => false,
        };
        if (shouldAbort)
            return (string.Empty, Array.Empty<string>(), null);

        var show = await ApiManager.GetShowInfoForSeries(seriesId).ConfigureAwait(false);
        if (show is null)
            return (string.Empty, Array.Empty<string>(), null);

        var file = await ApiManager.GetFileInfo(fileId, seriesId).ConfigureAwait(false);
        var (episode, episodeXref, _) = (file?.EpisodeList ?? new()).FirstOrDefault();
        if (file is null || episode is null)
            return (string.Empty, Array.Empty<string>(), null);

        if (season is null || episode is null)
            return (string.Empty, Array.Empty<string>(), null);

        var showName = show.DefaultSeason.AniDB.Title?.ReplaceInvalidPathCharacters() ?? $"Shoko Series {show.Id}";
        var episodeNumber = Ordering.GetEpisodeNumber(show, season, episode);
        var episodeName = (episode.AniDB.Titles.FirstOrDefault(t => t.LanguageCode == "en")?.Value ?? $"Episode {episode.AniDB.Type} {episodeNumber}").ReplaceInvalidPathCharacters();

        // For those **really** long names we have to cut if off at some point…
        if (showName.Length >= NameCutOff)
            showName = showName[..NameCutOff].Split(' ').SkipLast(1).Join(' ') + "…";
        if (episodeName.Length >= NameCutOff)
            episodeName = episodeName[..NameCutOff].Split(' ').SkipLast(1).Join(' ') + "…";

        var isExtra = file.EpisodeList.Any(eI => season.IsExtraEpisode(eI.Episode));
        var folders = new List<string>();
        var extrasFolders = file.ExtraType switch {
            null => isExtra ? new string[] { "extras" } : null,
            ExtraType.ThemeSong => new string[] { "theme-music" },
            ExtraType.ThemeVideo => config.AddCreditsAsThemeVideos && config.AddCreditsAsSpecialFeatures
                ? new string[] { "backdrops", "extras" }
                : config.AddCreditsAsThemeVideos
                ? new string[] { "backdrops" }
                : config.AddCreditsAsSpecialFeatures
                ? new string[] { "extras" }
                : new string[] {  },
            ExtraType.Trailer => config.AddTrailers
                ? new string[] { "trailers" }
                : new string[] {  },
            ExtraType.BehindTheScenes => new string[] { "behind the scenes" },
            ExtraType.DeletedScene => new string[] { "deleted scenes" },
            ExtraType.Clip => new string[] { "clips" },
            ExtraType.Interview => new string[] { "interviews" },
            ExtraType.Scene => new string[] { "scenes" },
            ExtraType.Sample => new string[] { "samples" },
            _ => new string[] { "extras" },
        };
        var filePartSuffix = (episodeXref.Percentage?.Group ?? 1) is not 1
            ? $".pt{episode.Shoko.CrossReferences.Where(xref => xref.ReleaseGroup == episodeXref.ReleaseGroup && xref.Percentage!.Group == episodeXref.Percentage!.Group).ToList().FindIndex(xref => xref.Percentage!.Start == episodeXref.Percentage!.Start && xref.Percentage!.End == episodeXref.Percentage!.End) + 1}"
            : "";
        if (isMovieSeason && collectionType is not CollectionType.TvShows) {
            if (extrasFolders != null) {
                foreach (var extrasFolder in extrasFolders)
                    foreach (var episodeInfo in season.EpisodeList)
                        folders.Add(Path.Join(vfsPath, $"{showName} [{ShokoSeriesId.Name}={show.Id}] [{ShokoEpisodeId.Name}={episodeInfo.Id}]", extrasFolder));
            }
            else {
                folders.Add(Path.Join(vfsPath, $"{showName} [{ShokoSeriesId.Name}={show.Id}] [{ShokoEpisodeId.Name}={episode.Id}]"));
                episodeName = "Movie";
            }
        }
        else {
            var isSpecial = show.IsSpecial(episode);
            var seasonNumber = Ordering.GetSeasonNumber(show, season, episode);
            var seasonFolder = $"Season {(isSpecial ? 0 : seasonNumber).ToString().PadLeft(2, '0')}";
            var showFolder = $"{showName} [{ShokoSeriesId.Name}={show.Id}]";
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
                episodeName = $"{showName} S{(isSpecial ? 0 : seasonNumber).ToString().PadLeft(2, '0')}E{episodeNumber.ToString().PadLeft(show.EpisodePadding, '0')}{filePartSuffix}";
            }
        }

        var fileName = $"{episodeName} [{ShokoSeriesId.Name}={seriesId}] [{ShokoFileId.Name}={fileId}]{Path.GetExtension(sourceLocation)}";
        var symbolicLinks = folders
            .Select(folderPath => Path.Join(folderPath, fileName))
            .ToArray();

        foreach (var symbolicLink in symbolicLinks)
            ApiManager.AddFileLookupIds(symbolicLink, fileId, seriesId, file.EpisodeList.Select(episode => episode.Id));
        return (sourceLocation, symbolicLinks, (file.Shoko.ImportedAt ?? file.Shoko.CreatedAt).ToLocalTime());
    }

// TODO: Remove this for 10.9
#pragma warning disable IDE0060
    public LinkGenerationResult GenerateSymbolicLinks(string sourceLocation, string[] symbolicLinks, DateTime importedAt)
#pragma warning restore IDE0060
    {
        try {
            var result = new LinkGenerationResult();
            var sourcePrefixLength = sourceLocation.Length - Path.GetExtension(sourceLocation).Length;
            var subtitleLinks = FindSubtitlesForPath(sourceLocation);
            foreach (var symbolicLink in symbolicLinks) {
                var symbolicDirectory = Path.GetDirectoryName(symbolicLink)!;
                if (!Directory.Exists(symbolicDirectory))
                    Directory.CreateDirectory(symbolicDirectory);

                result.Paths.Add(symbolicLink);
                if (!File.Exists(symbolicLink)) {
                    result.CreatedVideos++;
                    Logger.LogDebug("Linking {Link} → {LinkTarget}", symbolicLink, sourceLocation);
                    // In case Jellyfin decided to run the resolver in parallel for whatever reason, then check again.
                    try {
                        File.CreateSymbolicLink(symbolicLink, sourceLocation);
                    }
                    catch {
                        if (!File.Exists(symbolicLink))
                            throw;
                    }
                    // TODO: Uncomment this for 10.9
                    // // Mock the creation date to fake the "date added" order in Jellyfin.
                    // File.SetCreationTime(symbolicLink, importedAt);
                }
                else {
                    var shouldFix = false;
                    try {
                        var nextTarget = File.ResolveLinkTarget(symbolicLink, false);
                        if (!string.Equals(sourceLocation, nextTarget?.FullName)) {
                            shouldFix = true;

                            Logger.LogWarning("Fixing broken symbolic link {Link} → {LinkTarget} (RealTarget={RealTarget})", symbolicLink, sourceLocation, nextTarget?.FullName);
                        }
                        // TODO: Uncomment this for 10.9
                        // var date = File.GetCreationTime(symbolicLink);
                        // if (date != importedAt) {
                        //     shouldFix = true;
                        //
                        //     Logger.LogWarning("Fixing broken symbolic link {Link} with incorrect date.", symbolicLink);
                        // }
                    }
                    catch (Exception ex) {
                        Logger.LogError(ex, "Encountered an error trying to resolve symbolic link {Link}", symbolicLink);
                        shouldFix = true;
                    }
                    if (shouldFix) {
                        // In case Jellyfin decided to run the resolver in parallel for whatever reason, then check again.
                        try {
                            File.Delete(symbolicLink);
                            File.CreateSymbolicLink(symbolicLink, sourceLocation);
                        }
                        catch {
                            if (!File.Exists(symbolicLink))
                                throw;
                        }
                        // TODO: Uncomment this for 10.9
                        // // Mock the creation date to fake the "date added" order in Jellyfin.
                        // File.SetCreationTime(symbolicLink, importedAt);
                        result.FixedVideos++;
                    }
                    else {
                        result.SkippedVideos++;
                    }
                }

                if (subtitleLinks.Count > 0) {
                    var symbolicName = Path.GetFileNameWithoutExtension(symbolicLink);
                    foreach (var subtitleSource in subtitleLinks) {
                        var extName = subtitleSource[sourcePrefixLength..];
                        var subtitleLink = Path.Join(symbolicDirectory, symbolicName + extName);

                        result.Paths.Add(subtitleLink);
                        if (!File.Exists(subtitleLink)) {
                            result.CreatedSubtitles++;
                            Logger.LogDebug("Linking {Link} → {LinkTarget}", subtitleLink, subtitleSource);
                            // In case Jellyfin decided to run the resolver in parallel for whatever reason, then check again.
                            try {
                                File.CreateSymbolicLink(subtitleLink, subtitleSource);
                            }
                            catch {
                                if (!File.Exists(subtitleLink))
                                    throw;
                            }
                        }
                        else {
                            var shouldFix = false;
                            try {
                                var nextTarget = File.ResolveLinkTarget(subtitleLink, false);
                                if (!string.Equals(subtitleSource, nextTarget?.FullName)) {
                                    shouldFix = true;

                                    Logger.LogWarning("Fixing broken symbolic link {Link} → {LinkTarget} (RealTarget={RealTarget})", subtitleLink, subtitleSource, nextTarget?.FullName);
                                }
                            }
                            catch (Exception ex) {
                                Logger.LogError(ex, "Encountered an error trying to resolve symbolic link {Link} for {LinkTarget}", subtitleLink, subtitleSource);
                                shouldFix = true;
                            }
                            if (shouldFix) {
                                // In case Jellyfin decided to run the resolver in parallel for whatever reason, then check again.
                                try {
                                    File.Delete(subtitleLink);
                                    File.CreateSymbolicLink(subtitleLink, subtitleSource);
                                }
                                catch {
                                    if (!File.Exists(subtitleLink))
                                        throw;
                                }
                                result.FixedSubtitles++;
                            }
                            else {
                                result.SkippedSubtitles++;
                            }
                        }
                    }
                }
            }

            return result;
        }
        catch (Exception ex) {
            Logger.LogError(ex, "An error occurred while trying to generate {LinkCount} links for {SourceLocation}; {ErrorMessage}", symbolicLinks.Length, sourceLocation, ex.Message);
            throw;
        }
    }

    private List<string> FindSubtitlesForPath(string sourcePath)
    {
        var externalPaths = new List<string>();
        var folderPath = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrEmpty(folderPath) || !FileSystem.DirectoryExists(folderPath))
            return externalPaths;

        var files = FileSystem.GetFilePaths(folderPath)
            .Except(new[] { sourcePath })
            .ToList();
        var sourcePrefix = Path.GetFileNameWithoutExtension(sourcePath);
        foreach (var file in files) {
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);
            if (
                fileNameWithoutExtension.Length >= sourcePrefix.Length &&
                sourcePrefix.Equals(fileNameWithoutExtension[..sourcePrefix.Length], StringComparison.OrdinalIgnoreCase) &&
                (fileNameWithoutExtension.Length == sourcePrefix.Length || NamingOptions.MediaFlagDelimiters.Contains(fileNameWithoutExtension[sourcePrefix.Length]))
            ) {
                var externalPathInfo = ExternalPathParser.ParseFile(file, fileNameWithoutExtension[sourcePrefix.Length..].ToString());
                if (externalPathInfo is not null && !string.IsNullOrEmpty(externalPathInfo.Path))
                    externalPaths.Add(externalPathInfo.Path);
            }
        }

        return externalPaths;
    }

    private LinkGenerationResult CleanupStructure(string vfsPath, string directoryToClean, IReadOnlyList<string> allKnownPaths)
    {
        Logger.LogDebug("Looking for files to remove in folder at {Path}", directoryToClean);
        var start = DateTime.Now;
        var previousStep = start;
        var result = new LinkGenerationResult();
        var searchFiles = NamingOptions.VideoFileExtensions.Concat(NamingOptions.SubtitleFileExtensions).Append(".nfo").ToHashSet();
        var toBeRemoved = FileSystem.GetFilePaths(directoryToClean, true)
            .Select(path => (path, extName: Path.GetExtension(path)))
            .Where(tuple => !string.IsNullOrEmpty(tuple.extName) && searchFiles.Contains(tuple.extName))
            .ExceptBy(allKnownPaths.ToHashSet(), tuple => tuple.path)
            .ToList();

        var nextStep = DateTime.Now;
        Logger.LogDebug("Found {FileCount} files to remove in {DirectoryToClean} in {TimeSpent}", toBeRemoved.Count, directoryToClean, nextStep - previousStep);
        previousStep = nextStep;

        foreach (var (location, extName) in toBeRemoved) {
            if (extName is ".nfo") {
                try {
                    Logger.LogTrace("Removing NFO file at {Path}", location);
                    File.Delete(location);
                }
                catch (Exception ex) {
                    Logger.LogError(ex, "Encountered an error trying to remove {FilePath}", location);
                    continue;
                }
                result.RemovedNfos++;
            }
            else if (NamingOptions.SubtitleFileExtensions.Contains(extName)) {
                if (TryMoveSubtitleFile(allKnownPaths, location)) {
                    result.FixedSubtitles++;
                    continue;
                }

                try {
                    Logger.LogTrace("Removing subtitle file at {Path}", location);
                    File.Delete(location);
                }
                catch (Exception ex) {
                    Logger.LogError(ex, "Encountered an error trying to remove {FilePath}", location);
                    continue;
                }
                result.RemovedSubtitles++;
            }
            else {
                if (ShouldIgnoreVideo(vfsPath, location)) {
                    result.SkippedVideos++;
                    continue;
                }

                try {
                    Logger.LogTrace("Removing video file at {Path}", location);
                    File.Delete(location);
                }
                catch (Exception ex) {
                    Logger.LogError(ex, "Encountered an error trying to remove {FilePath}", location);
                    continue;
                }
                result.RemovedVideos++;
            }
        }

        nextStep = DateTime.Now;
        Logger.LogTrace("Removed {FileCount} files in {DirectoryToClean} in {TimeSpent} (Total={TotalSpent})", result.Removed, directoryToClean, nextStep - previousStep, nextStep - start);
        previousStep = nextStep;

        var cleaned = 0;
        var directoriesToClean = toBeRemoved
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
        Logger.LogDebug("Found {DirectoryCount} directories to potentially clean in {DirectoryToClean} in {TimeSpent} (Total={TotalSpent})", toBeRemoved.Count, directoryToClean, nextStep - previousStep, nextStep - start);
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

    private static bool TryMoveSubtitleFile(IReadOnlyList<string> allKnownPaths, string subtitlePath)
    {
        if (!TryGetIdsForPath(subtitlePath, out var seriesId, out var fileId))
            return false;

        var symbolicLink = allKnownPaths.FirstOrDefault(knownPath => TryGetIdsForPath(knownPath, out var knownSeriesId, out var knownFileId) && seriesId == knownSeriesId && fileId == knownFileId);
        if (string.IsNullOrEmpty(symbolicLink))
            return false;

        var sourcePathWithoutExt = symbolicLink[..^Path.GetExtension(symbolicLink).Length];
        if (!subtitlePath.StartsWith(sourcePathWithoutExt))
            return false;

        var extName = subtitlePath[sourcePathWithoutExt.Length..];
        string? realTarget = null;
        try {
            realTarget = File.ResolveLinkTarget(symbolicLink, false)?.FullName;
        }
        catch { }
        if (string.IsNullOrEmpty(realTarget))
            return false;

        var realSubtitlePath = realTarget[..^Path.GetExtension(realTarget).Length] + extName;
        if (!File.Exists(realSubtitlePath))
            File.Move(subtitlePath, realSubtitlePath);
        else
            File.Delete(subtitlePath);
        File.CreateSymbolicLink(subtitlePath, realSubtitlePath);

        return true;
    }

    private static bool ShouldIgnoreVideo(string vfsPath, string path)
    {
        // Ignore the video if it's within one of the folders to potentially ignore _and_ it doesn't have any shoko ids set.
        var parentDirectories = path[(vfsPath.Length + 1)..].Split(Path.DirectorySeparatorChar).SkipLast(1).ToArray();
        return parentDirectories.Length > 1 && IgnoreFolderNames.Contains(parentDirectories.Last()) && !TryGetIdsForPath(path, out _, out _);
    }

    public static bool TryGetIdsForPath(string path, [NotNullWhen(true)] out string? seriesId, [NotNullWhen(true)] out string? fileId)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (!fileName.TryGetAttributeValue(ShokoFileId.Name, out fileId) || !int.TryParse(fileId, out _) ||
            !fileName.TryGetAttributeValue(ShokoSeriesId.Name, out seriesId) || !int.TryParse(seriesId, out _)) {
            seriesId = null;
            fileId = null;
            return false;
        }

        return true;
    }

    #endregion
}
