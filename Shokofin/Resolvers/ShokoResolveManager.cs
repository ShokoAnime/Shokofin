using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Naming.Common;
using Emby.Naming.ExternalFiles;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Models;
using Shokofin.Configuration;
using Shokofin.ExternalIds;
using Shokofin.Utils;

using File = System.IO.File;
using TvSeries = MediaBrowser.Controller.Entities.TV.Series;

namespace Shokofin.Resolvers;

public class ShokoResolveManager
{
    private readonly ShokoAPIManager ApiManager;

    private readonly ShokoAPIClient ApiClient;

    private readonly IIdLookup Lookup;

    private readonly ILibraryManager LibraryManager;

    private readonly IFileSystem FileSystem;

    private readonly ILogger<ShokoResolveManager> Logger;

    private readonly NamingOptions _namingOptions;

    private readonly ExternalPathParser ExternalPathParser;

    private GuardedMemoryCache DataCache = new(new MemoryCacheOptions() {
        ExpirationScanFrequency = ExpirationScanFrequency,
    });

    private static readonly TimeSpan ExpirationScanFrequency = new(0, 25, 0);

    private static readonly TimeSpan DefaultTTL = TimeSpan.FromMinutes(60);

    public ShokoResolveManager(
        ShokoAPIManager apiManager,
        ShokoAPIClient apiClient,
        IIdLookup lookup,
        ILibraryManager libraryManager,
        IFileSystem fileSystem,
        ILogger<ShokoResolveManager> logger,
        ILocalizationManager localizationManager,
        NamingOptions namingOptions
    )
    {
        ApiManager = apiManager;
        ApiClient = apiClient;
        Lookup = lookup;
        LibraryManager = libraryManager;
        FileSystem = fileSystem;
        Logger = logger;
        _namingOptions = namingOptions;
        ExternalPathParser = new ExternalPathParser(namingOptions, localizationManager, MediaBrowser.Model.Dlna.DlnaProfileType.Subtitle);
        LibraryManager.ItemRemoved += OnLibraryManagerItemRemoved;
    }

    ~ShokoResolveManager()
    {
        LibraryManager.ItemRemoved -= OnLibraryManagerItemRemoved;
        Clear(false);
    }

    public void Clear(bool restore = true)
    {
        Logger.LogDebug("Clearing data…");
        DataCache.Dispose();
        if (restore) {
            Logger.LogDebug("Initialising new cache…");
            DataCache = new(new MemoryCacheOptions() {
                ExpirationScanFrequency = ExpirationScanFrequency,
            });
        }
    }

    #region Changes Tracking

    private void OnLibraryManagerItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        // Remove the VFS directory for any media library folders when they're removed.
        var root = LibraryManager.RootFolder;
        if (e.Item != null && root != null && e.Item != root && e.Item is Folder folder && folder.ParentId == Guid.Empty  && !string.IsNullOrEmpty(folder.Path) && !folder.Path.StartsWith(root.Path)) {
            DataCache.Remove(folder.Id.ToString());
            var mediaFolderConfig = Plugin.Instance.Configuration.MediaFolders.FirstOrDefault(c => c.MediaFolderId == folder.Id);
            if (mediaFolderConfig != null) {
                Logger.LogDebug(
                    "Removing stored configuration for folder at {Path} (ImportFolder={ImportFolderId},RelativePath={RelativePath})",
                    folder.Path,
                    mediaFolderConfig.ImportFolderId,
                    mediaFolderConfig.ImportFolderRelativePath
                );
                Plugin.Instance.Configuration.MediaFolders.Remove(mediaFolderConfig);
                Plugin.Instance.SaveConfiguration();
            }
            var vfsPath = ShokoAPIManager.GetVirtualRootForMediaFolder(folder);
            if (Directory.Exists(vfsPath)) {
                Logger.LogInformation("Removing VFS directory for folder at {Path}", folder.Path);
                Directory.Delete(vfsPath, true);
                Logger.LogInformation("Removed VFS directory for folder at {Path}", folder.Path);
            }
        }
    }

    #endregion

    #region Media Folder Mapping

    public async Task<MediaFolderConfiguration> GetOrCreateConfigurationForMediaFolder(Folder mediaFolder)
    {
        var config = Plugin.Instance.Configuration;
        var mediaFolderConfig = config.MediaFolders.FirstOrDefault(c => c.MediaFolderId == mediaFolder.Id);
        if (mediaFolderConfig != null)
            return mediaFolderConfig;

        // Check if we should introduce the VFS for the media folder.
        mediaFolderConfig = new() {
            MediaFolderId = mediaFolder.Id,
            MediaFolderPath = mediaFolder.Path,
            IsVirtualFileSystemEnabled = config.VirtualFileSystem,
            IsLibraryFilteringEnabled = config.LibraryFiltering,
            IsFileEventsEnabled = config.SignalR_FileEvents,
            IsRefreshEventsEnabled = config.SignalR_RefreshEnabled,
        };

        var start = DateTime.UtcNow;
        var attempts = 0;
        var samplePaths = FileSystem.GetFilePaths(mediaFolder.Path, true)
            .Where(path => _namingOptions.VideoFileExtensions.Contains(Path.GetExtension(path)))
            .Take(100)
            .ToList();

        Logger.LogDebug("Asking remote server if it knows any of the {Count} sampled files in {Path}.", samplePaths.Count > 100 ? 100 : samplePaths.Count, mediaFolder.Path);
        foreach (var path in samplePaths) {
            attempts++;
            var partialPath = path[mediaFolder.Path.Length..];
            var files = await ApiClient.GetFileByPath(partialPath).ConfigureAwait(false);
            var file = files.FirstOrDefault();
            if (file == null)
                continue;

            var fileId = file.Id.ToString();
            var fileLocations = file.Locations
                .Where(location => location.Path.EndsWith(partialPath))
                .ToList();
            if (fileLocations.Count == 0)
                continue;

            var fileLocation = fileLocations[0];
            mediaFolderConfig.ImportFolderId = fileLocation.ImportFolderId;
            mediaFolderConfig.ImportFolderRelativePath = fileLocation.Path[..^partialPath.Length];
            break;
        }

        try {
            var importFolder = await ApiClient.GetImportFolder(mediaFolderConfig.ImportFolderId);
            if (importFolder != null)
                mediaFolderConfig.ImportFolderName = importFolder.Name;
        }
        catch { }

        // Store and log the result.
        config.MediaFolders.Add(mediaFolderConfig);
        Plugin.Instance.SaveConfiguration(config);
        if (mediaFolderConfig.IsMapped) {
            Logger.LogInformation(
                "Found a match for media folder at {Path} in {TimeSpan} (ImportFolder={FolderId},RelativePath={RelativePath},MediaLibrary={Path},Attempts={Attempts})",
                mediaFolder.Path,
                DateTime.UtcNow - start,
                mediaFolderConfig.ImportFolderId,
                mediaFolderConfig.ImportFolderRelativePath,
                mediaFolder.Path,
                attempts
            );
        }
        else {
            Logger.LogWarning(
                "Failed to find a match for media folder at {Path} after {Amount} attempts in {TimeSpan}.",
                mediaFolder.Path,
                attempts, 
                DateTime.UtcNow - start
            );
        }

        return mediaFolderConfig;
    }

    #endregion

    #region Generate Structure

    /// <summary>
    /// Generates the VFS structure if the VFS is enabled globally or on the
    /// <paramref name="mediaFolder"/>.
    /// </summary>
    /// <param name="mediaFolder">The media folder to generate a structure for.</param>
    /// <param name="folderPath">The folder within the media folder to generate a structure for.</param>
    /// <returns>The VFS path, if it succeeded.</returns>
    private Task<string?> GenerateStructureForFolderInVFS(Folder mediaFolder)
        => DataCache.GetOrCreateAsync(
            mediaFolder.Path,
            async (_) => {
                var mediaConfig = await GetOrCreateConfigurationForMediaFolder(mediaFolder);
                if (!mediaConfig.IsMapped)
                    return null;

                // Return early if we're not going to generate them.
                if (!mediaConfig.IsVirtualFileSystemEnabled)
                    return null;

                // Check if we should introduce the VFS for the media folder.
                var start = DateTime.UtcNow;
                var allPaths = FileSystem.GetFilePaths(mediaFolder.Path, true)
                    .Where(path => _namingOptions.VideoFileExtensions.Contains(Path.GetExtension(path)))
                    .ToHashSet();
                Logger.LogDebug("Found {FileCount} files in media folder at {Path} in {TimeSpan}.", allPaths.Count, mediaFolder.Path, DateTime.UtcNow - start);

                var vfsPath = ShokoAPIManager.GetVirtualRootForMediaFolder(mediaFolder);
                var allFiles = GetImportFolderFiles(mediaConfig.ImportFolderId, mediaConfig.ImportFolderRelativePath, mediaFolder.Path, allPaths);
                await GenerateSymbolicLinks(mediaFolder, allFiles).ConfigureAwait(false);

                return vfsPath;
            },
            new() {
                AbsoluteExpirationRelativeToNow = DefaultTTL,
            }
        );

    private IEnumerable<(string sourceLocation, string fileId, string seriesId)> GetImportFolderFiles(int importFolderId, string importFolderSubPath, string mediaFolderPath, ISet<string> fileSet)
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
                var location = file.Locations
                    .Where(location => location.ImportFolderId == importFolderId && (importFolderSubPath.Length == 0 || location.Path.StartsWith(importFolderSubPath)))
                    .FirstOrDefault();
                if (location == null || file.CrossReferences.Count == 0)
                    continue;

                var sourceLocation = Path.Join(mediaFolderPath, location.Path[importFolderSubPath.Length..]);
                if (!fileSet.Contains(sourceLocation))
                    continue;

                // Yield all single-series files now, and offset the processing of all multi-series files for later.
                var seriesIds = file.CrossReferences.Select(x => x.Series.Shoko).ToHashSet();
                if (seriesIds.Count == 1) {
                    totalSingleSeriesFiles++;
                    singleSeriesIds.Add(seriesIds.First());
                    foreach (var xref in file.CrossReferences)
                        yield return (sourceLocation, fileId: file.Id.ToString(), seriesId: xref.Series.Shoko.ToString());
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
        foreach (var (file, sourceLocation) in multiSeriesFiles) {
            var crossReferences = file.CrossReferences
                .Where(xref => singleSeriesIds.Contains(xref.Series.Shoko))
                .ToList();
            foreach (var xref in crossReferences)
                yield return (sourceLocation, fileId: file.Id.ToString(), seriesId: xref.Series.Shoko.ToString());
            totalMultiSeriesFiles += crossReferences.Count;
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

    private async Task GenerateSymbolicLinks(Folder mediaFolder, IEnumerable<(string sourceLocation, string fileId, string seriesId)> files)
    {
        var start = DateTime.UtcNow;
        var skippedLinks = 0;
        var fixedLinks = 0;
        var subtitles = 0;
        var fixedSubtitles = 0;
        var skippedSubtitles = 0;
        var vfsPath = ShokoAPIManager.GetVirtualRootForMediaFolder(mediaFolder);
        var collectionType = LibraryManager.GetInheritedContentType(mediaFolder);
        var allPathsForVFS = new ConcurrentBag<(string sourceLocation, string symbolicLink)>();
        var semaphore = new SemaphoreSlim(Plugin.Instance.Configuration.VirtualFileSystemThreads);
        await Task.WhenAll(files.Select(async (tuple) => {
            await semaphore.WaitAsync().ConfigureAwait(false);

            try {
                // Skip any source files we weren't meant to have in the library.
                var (sourceLocation, symbolicLinks) = await GenerateLocationsForFile(vfsPath, collectionType, tuple.sourceLocation, tuple.fileId, tuple.seriesId).ConfigureAwait(false);
                if (string.IsNullOrEmpty(sourceLocation))
                    return;

                var sourcePrefix = Path.GetFileNameWithoutExtension(sourceLocation);
                var sourcePrefixLength = sourceLocation.Length - Path.GetExtension(sourceLocation).Length;
                var subtitleLinks = FindSubtitlesForPath(sourceLocation);
                foreach (var symbolicLink in symbolicLinks) {
                    var symbolicDirectory = Path.GetDirectoryName(symbolicLink)!;
                    if (!Directory.Exists(symbolicDirectory))
                        Directory.CreateDirectory(symbolicDirectory);

                    allPathsForVFS.Add((sourceLocation, symbolicLink));
                    if (!File.Exists(symbolicLink)) {
                        Logger.LogDebug("Linking {Link} → {LinkTarget}", symbolicLink, sourceLocation);
                        File.CreateSymbolicLink(symbolicLink, sourceLocation);
                    }
                    else {
                        var shouldFix = false;
                        try {
                            var nextTarget = File.ResolveLinkTarget(symbolicLink, false);
                            if (!string.Equals(sourceLocation, nextTarget?.FullName)) {
                                shouldFix = true;

                                Logger.LogWarning("Fixing broken symbolic link {Link} → {LinkTarget} (RealTarget={RealTarget})", symbolicLink, sourceLocation, nextTarget?.FullName);
                            }
                        }
                        catch (Exception ex) {
                            Logger.LogError(ex, "Encountered an error trying to resolve symbolic link {Link}", symbolicLink);
                            shouldFix = true;
                        }
                        if (shouldFix) {
                            File.Delete(symbolicLink);
                            File.CreateSymbolicLink(symbolicLink, sourceLocation);
                            fixedLinks++;
                        }
                        else {
                            skippedLinks++;
                        }
                    }

                    if (subtitleLinks.Count > 0) {
                        var symbolicName = Path.GetFileNameWithoutExtension(symbolicLink);
                        foreach (var subtitleSource in subtitleLinks) {
                            var extName = subtitleSource[sourcePrefixLength..];
                            var subtitleLink = Path.Combine(symbolicDirectory, symbolicName + extName);

                            subtitles++;
                            allPathsForVFS.Add((subtitleSource, subtitleLink));
                            if (!File.Exists(subtitleLink)) {
                                Logger.LogDebug("Linking {Link} → {LinkTarget}", subtitleLink, subtitleSource);
                                File.CreateSymbolicLink(subtitleLink, subtitleSource);
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
                                    File.Delete(subtitleLink);
                                    File.CreateSymbolicLink(subtitleLink, subtitleSource);
                                    fixedSubtitles++;
                                }
                                else {
                                    skippedSubtitles++;
                                }
                            }
                        }
                    }
                }
            }
            finally {
                semaphore.Release();
            }
        }))
            .ConfigureAwait(false);

        var removedLinks = 0;
        var removedSubtitles = 0;
        var toBeRemoved = FileSystem.GetFilePaths(vfsPath, true)
            .Select(path => (path, extName: Path.GetExtension(path)))
            .Where(tuple => _namingOptions.VideoFileExtensions.Contains(tuple.extName) || _namingOptions.SubtitleFileExtensions.Contains(tuple.extName))
            .ExceptBy(allPathsForVFS.Select(tuple => tuple.symbolicLink).ToHashSet(), tuple => tuple.path)
            .ToList();
        foreach (var (symbolicLink, extName) in toBeRemoved) {
            // Continue in case we already removed the (subtitle) file.
            if (!File.Exists(symbolicLink))
                continue;

            File.Delete(symbolicLink);

            // Stats tracking.
            if (_namingOptions.VideoFileExtensions.Contains(extName)) {
                var subtitleLinks = _namingOptions.VideoFileExtensions.Contains(Path.GetExtension(symbolicLink)) ? FindSubtitlesForPath(symbolicLink) : Array.Empty<string>();

                removedLinks++;
                foreach (var subtitleLink in subtitleLinks) {
                    removedSubtitles++;
                    File.Delete(symbolicLink);
                }
            }
            else {
                removedSubtitles++;
            }

            CleanupDirectoryStructure(symbolicLink);
        }

        var timeSpent = DateTime.UtcNow - start;
        Logger.LogInformation(
            "Created {CreatedMedia} ({CreatedSubtitles}), fixed {FixedMedia} ({FixedSubtitles}), skipped {SkippedMedia} ({SkippedSubtitles}), and removed {RemovedMedia} ({RemovedSubtitles}) symbolic links in media folder at {Path} in {TimeSpan}",
            allPathsForVFS.Count - skippedLinks - fixedLinks - subtitles,
            subtitles - fixedSubtitles - skippedSubtitles,
            fixedLinks,
            fixedSubtitles,
            skippedLinks,
            skippedSubtitles,
            toBeRemoved.Count,
            removedSubtitles,
            mediaFolder.Path,
            timeSpent
        );
    }

    // Note: Out of the 14k entries in my test shoko database, then only **319** entries have a title longer than 100 chacters.
    private const int NameCutOff = 64;

    private async Task<(string sourceLocation, string[] symbolicLinks)> GenerateLocationsForFile(string vfsPath, string? collectionType, string sourceLocation, string fileId, string seriesId)
    {
        var season = await ApiManager.GetSeasonInfoForSeries(seriesId).ConfigureAwait(false);
        if (season == null)
            return (sourceLocation: string.Empty, symbolicLinks: Array.Empty<string>());

        var isMovieSeason = season.Type == SeriesType.Movie;
        var shouldAbort = collectionType switch {
            CollectionType.TvShows => isMovieSeason && Plugin.Instance.Configuration.SeparateMovies,
            CollectionType.Movies => !isMovieSeason,
            _ => false,
        };
        if (shouldAbort)
            return (sourceLocation: string.Empty, symbolicLinks: Array.Empty<string>());

        var show = await ApiManager.GetShowInfoForSeries(seriesId).ConfigureAwait(false);
        if (show == null)
            return (sourceLocation: string.Empty, symbolicLinks: Array.Empty<string>());

        var file = await ApiManager.GetFileInfo(fileId, seriesId).ConfigureAwait(false);
        var episode = file?.EpisodeList.FirstOrDefault();
        if (file == null || episode == null)
            return (sourceLocation: string.Empty, symbolicLinks: Array.Empty<string>());

        if (season == null || episode == null)
            return (sourceLocation: string.Empty, symbolicLinks: Array.Empty<string>());

        var showName = show.DefaultSeason.AniDB.Title?.ReplaceInvalidPathCharacters() ?? $"Shoko Series {show.Id}";
        var episodeNumber = Ordering.GetEpisodeNumber(show, season, episode);
        var episodeName = (episode.AniDB.Titles.FirstOrDefault(t => t.LanguageCode == "en")?.Value ?? $"Episode {episode.AniDB.Type} {episodeNumber}").ReplaceInvalidPathCharacters();

        // For those **really** long names we have to cut if off at some point…
        if (showName.Length >= NameCutOff)
            showName = showName[..NameCutOff].Split(' ').SkipLast(1).Join(' ') + "…";
        if (episodeName.Length >= NameCutOff)
            episodeName = episodeName[..NameCutOff].Split(' ').SkipLast(1).Join(' ') + "…";

        var folders = new List<string>();
        var extrasFolder = file.ExtraType switch {
            null => null,
            ExtraType.ThemeSong => "theme-music",
            ExtraType.ThemeVideo => "backdrops",
            ExtraType.Trailer => "trailers",
            _ => "extras",
        };
        var fileNameSuffix = file.ExtraType switch {
            ExtraType.BehindTheScenes => "-behindthescenes",
            ExtraType.Clip => "-clip",
            ExtraType.DeletedScene => "-deletedscene",
            ExtraType.Interview => "-interview",
            ExtraType.Scene => "-scene",
            ExtraType.Sample => "-other",
            ExtraType.Unknown => "-other",
            _ => string.Empty,
        };
        if (isMovieSeason && collectionType != CollectionType.TvShows) {
            if (!string.IsNullOrEmpty(extrasFolder)) {
                foreach (var episodeInfo in season.EpisodeList)
                    folders.Add(Path.Combine(vfsPath, $"{showName} [{ShokoSeriesId.Name}={show.Id}] [{ShokoEpisodeId.Name}={episodeInfo.Id}]", extrasFolder));
            }
            else {
                folders.Add(Path.Combine(vfsPath, $"{showName} [{ShokoSeriesId.Name}={show.Id}] [{ShokoEpisodeId.Name}={episode.Id}]"));
                episodeName = "Movie";
            }
        }
        else {
            var isSpecial = show.IsSpecial(episode);
            var seasonNumber = isSpecial ? 0 : Ordering.GetSeasonNumber(show, season, episode);
            var seasonName = $"Season {(isSpecial ? 0 : seasonNumber).ToString().PadLeft(2, '0')}";
            if (!string.IsNullOrEmpty(extrasFolder)) {
                folders.Add(Path.Combine(vfsPath, $"{showName} [{ShokoSeriesId.Name}={show.Id}]", extrasFolder));
                folders.Add(Path.Combine(vfsPath, $"{showName} [{ShokoSeriesId.Name}={show.Id}]", seasonName, extrasFolder));
            }
            else {
                folders.Add(Path.Combine(vfsPath, $"{showName} [{ShokoSeriesId.Name}={show.Id}]", seasonName));
                episodeName = $"{showName} S{(isSpecial ? 0 : seasonNumber).ToString().PadLeft(2, '0')}E{episodeNumber}";
            }
        }

        var fileName = $"{episodeName} [{ShokoSeriesId.Name}={seriesId}] [{ShokoFileId.Name}={fileId}]{fileNameSuffix}{Path.GetExtension(sourceLocation)}";
        var symbolicLinks = folders
            .Select(folderPath => Path.Combine(folderPath, fileName))
            .ToArray();

        foreach (var symbolicLink in symbolicLinks)
            ApiManager.AddFileLookupIds(symbolicLink, fileId, seriesId, file.EpisodeList.Select(episode => episode.Id));
        return (sourceLocation, symbolicLinks);
    }

    private static void CleanupDirectoryStructure(string? path)
    {
        path = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(path) && Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) {
            Directory.Delete(path);
            path = Path.GetDirectoryName(path);
        }
    }
    
    private IReadOnlyList<string> FindSubtitlesForPath(string sourcePath)
    {
        var externalPaths = new List<string>();
        var folderPath = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrEmpty(folderPath) || !FileSystem.DirectoryExists(folderPath))
            return externalPaths;

        var files = FileSystem.GetFilePaths(folderPath)
            .ToList();
        files.Remove(sourcePath);

        if (files.Count == 0)
            return externalPaths;

        var sourcePrefix = Path.GetFileNameWithoutExtension(sourcePath);
        foreach (var file in files) {
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);
            if (
                fileNameWithoutExtension.Length >= sourcePrefix.Length &&
                sourcePrefix.Equals(fileNameWithoutExtension[..sourcePrefix.Length], StringComparison.OrdinalIgnoreCase) &&
                (fileNameWithoutExtension.Length == sourcePrefix.Length || _namingOptions.MediaFlagDelimiters.Contains(fileNameWithoutExtension[sourcePrefix.Length]))
            ) {
                var externalPathInfo = ExternalPathParser.ParseFile(file, fileNameWithoutExtension[sourcePrefix.Length..].ToString());
                if (externalPathInfo != null && !string.IsNullOrEmpty(externalPathInfo.Path))
                    externalPaths.Add(externalPathInfo.Path);
            }
        }

        return externalPaths;
    }

    #endregion

    #region Ignore Rule

    public async Task<bool> ShouldFilterItem(Folder? parent, FileSystemMetadata fileInfo)
    {
        // Check if the parent is not made yet, or the file info is missing.
        if (parent == null || fileInfo == null)
            return false;

        // Check if the root is not made yet. This should **never** be false at
        // this point in time, but if it is, then bail.
        var root = LibraryManager.RootFolder;
        if (root == null || parent.Id == root.Id)
            return false;

        // Assume anything within the VFS is already okay.
        if (fileInfo.FullName.StartsWith(Plugin.Instance.VirtualRoot))
            return false;

        try {
            // Enable the scanner if we selected to use the Shoko provider for any metadata type on the current root folder.
            if (!Lookup.IsEnabledForItem(parent, out var isSoleProvider))
                return false;

            if (fileInfo.IsDirectory && Plugin.Instance.IgnoredFolders.Contains(Path.GetFileName(fileInfo.FullName).ToLowerInvariant())) {
                Logger.LogDebug("Excluded folder at path {Path}", fileInfo.FullName);
                return true;
            }

            if (!fileInfo.IsDirectory && !_namingOptions.VideoFileExtensions.Contains(fileInfo.Extension.ToLowerInvariant())) {
                Logger.LogDebug("Skipped excluded file at path {Path}", fileInfo.FullName);
                return false;
            }

            var fullPath = fileInfo.FullName;
            var (mediaFolder, partialPath) = ApiManager.FindMediaFolder(fullPath, parent, root);

            // Ignore any media folders that aren't mapped to shoko.
            var mediaFolderConfig = await GetOrCreateConfigurationForMediaFolder(mediaFolder);
            if (!mediaFolderConfig.IsMapped) {
                Logger.LogDebug("Skipped media folder for path {Path} (MediaFolder={MediaFolderId})", fileInfo.FullName, mediaFolderConfig.MediaFolderId);
                return false;
            }

            // Abort now if the VFS is enabled, since it will take care of moving
            // from the physical library to the "virtual" library.
            if (parent.ParentId == root.Id && mediaFolderConfig.IsVirtualFileSystemEnabled)
                return false;

            var shouldIgnore = mediaFolderConfig.IsLibraryFilteringEnabled ?? mediaFolderConfig.IsVirtualFileSystemEnabled  || isSoleProvider;
            var collectionType = LibraryManager.GetInheritedContentType(mediaFolder);
            if (fileInfo.IsDirectory)
                return await ShouldFilterDirectory(partialPath, fullPath, collectionType, shouldIgnore).ConfigureAwait(false);
            else
                return await ShouldFilterFile(partialPath, fullPath, shouldIgnore).ConfigureAwait(false);
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
            throw;
        }
    }

    private async Task<bool> ShouldFilterDirectory(string partialPath, string fullPath, string? collectionType, bool shouldIgnore)
    {
        var season = await ApiManager.GetSeasonInfoByPath(fullPath).ConfigureAwait(false);

        // We inform/warn here since we enabled the provider in our library, but we can't find a match for the given folder path.
        if (season == null) {
            // If we're in strict mode, then check the sub-directories if we have a <Show>/<Season>/<Episodes> structure.
            if (shouldIgnore && partialPath[1..].Split(Path.DirectorySeparatorChar).Length == 1) {
                try {
                    var entries = FileSystem.GetDirectories(fullPath, false).ToList();
                    Logger.LogDebug("Unable to find shoko series for {Path}, trying {DirCount} sub-directories.", partialPath, entries.Count);
                    foreach (var entry in entries) {
                        season = await ApiManager.GetSeasonInfoByPath(entry.FullName).ConfigureAwait(false);
                        if (season != null) {
                            Logger.LogDebug("Found shoko series {SeriesName} for sub-directory of path {Path} (Series={SeriesId})", season.Shoko.Name, partialPath, season.Id);
                            break;
                        }
                    }
                }
                catch (DirectoryNotFoundException) { }
            }
            if (season == null) {
                if (shouldIgnore)
                    Logger.LogInformation("Ignored unknown folder at path {Path}", partialPath);
                else
                    Logger.LogWarning("Skipped unknown folder at path {Path}", partialPath);
                return shouldIgnore;
            }
        }

        // Filter library if we enabled the option.
        var isMovieSeason = season.Type == SeriesType.Movie;
        switch (collectionType) {
            case CollectionType.TvShows:
                if (isMovieSeason && Plugin.Instance.Configuration.SeparateMovies) {
                    Logger.LogInformation("Found movie in show library and library separation is enabled, ignoring shoko series. (Series={SeriesId})", season.Id);
                    return true;
                }
                break;
            case CollectionType.Movies:
                if (!isMovieSeason) {
                    Logger.LogInformation("Found show in movie library, ignoring shoko series. (Series={SeriesId})", season.Id);
                    return true;
                }
                break;
        }

        var show = await ApiManager.GetShowInfoForSeries(season.Id).ConfigureAwait(false)!;
        if (!string.IsNullOrEmpty(show!.GroupId))
            Logger.LogInformation("Found shoko group {GroupName} (Series={SeriesId},Group={GroupId})", show.Name, season.Id, show.GroupId);
        else
            Logger.LogInformation("Found shoko series {SeriesName} (Series={SeriesId})", season.Shoko.Name, season.Id);

        return false;
    }

    private async Task<bool> ShouldFilterFile(string partialPath, string fullPath, bool shouldIgnore)
    {
        var (file, season, _) = await ApiManager.GetFileInfoByPath(fullPath).ConfigureAwait(false);

        // We inform/warn here since we enabled the provider in our library, but we can't find a match for the given file path.
        if (file == null || season == null) {
            if (shouldIgnore)
                Logger.LogInformation("Ignored unknown file at path {Path}", partialPath);
            else
                Logger.LogWarning("Skipped unknown file at path {Path}", partialPath);
            return shouldIgnore;
        }

        Logger.LogInformation("Found {EpisodeCount} shoko episode(s) for {SeriesName} (Series={SeriesId},File={FileId})", file.EpisodeList.Count, season.Shoko.Name, season.Id, file.Id);

        // We're going to post process this file later, but we don't want to include it in our library for now.
        if (file.ExtraType != null) {
            Logger.LogInformation("File was assigned an extra type, ignoring file. (Series={SeriesId},File={FileId})", season.Id, file.Id);
            return true;
        }

        return false;
    }

    #endregion

    #region Resolvers

    public async Task<BaseItem?> ResolveSingle(Folder? parent, string? collectionType, FileSystemMetadata fileInfo)
    {
        if (!(collectionType == CollectionType.TvShows || collectionType == CollectionType.Movies || collectionType == null) || parent == null || fileInfo == null)
            return null;

        var root = LibraryManager.RootFolder;
        if (root == null || parent == root)
            return null;

        try {
            if (!Lookup.IsEnabledForItem(parent))
                return null;

            // We're already within the VFS, so let jellyfin take it from here.
            var fullPath = fileInfo.FullName;
            if (!fullPath.StartsWith(Plugin.Instance.VirtualRoot))
                return null;

            var (mediaFolder, _) = ApiManager.FindMediaFolder(fullPath, parent, root);
            if (mediaFolder == root)
                return null;

            var vfsPath = await GenerateStructureForFolderInVFS(mediaFolder).ConfigureAwait(false);
            if (string.IsNullOrEmpty(vfsPath))
                return null;

            if (parent.Id == mediaFolder.Id && fileInfo.IsDirectory) {
                if (!fileInfo.Name.TryGetAttributeValue(ShokoSeriesId.Name, out var seriesId) || !int.TryParse(seriesId, out _))
                    return null;

                return new TvSeries() {
                    Path = fileInfo.FullName,
                };
            }

            // TODO: Redirect to the base item in the VFS if needed.

            return null;
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
            throw;
        }
    }

    public async Task<MultiItemResolverResult?> ResolveMultiple(Folder? parent, string? collectionType, List<FileSystemMetadata> fileInfoList)
    {
        if (!(collectionType == CollectionType.TvShows || collectionType == CollectionType.Movies || collectionType == null) || parent == null)
            return null;

        var root = LibraryManager.RootFolder;
        if (root == null || parent == root)
            return null;

        try {
            if (!Lookup.IsEnabledForItem(parent))
                return null;

            // Redirect children of a VFS managed media folder to the VFS.
            if (parent.ParentId == root.Id) {
                var vfsPath = await GenerateStructureForFolderInVFS(parent).ConfigureAwait(false);
                if (string.IsNullOrEmpty(vfsPath))
                    return null;

                var createMovies = collectionType == CollectionType.Movies || (collectionType == null && Plugin.Instance.Configuration.SeparateMovies);
                var items = FileSystem.GetDirectories(vfsPath)
                    .AsParallel()
                    .SelectMany(dirInfo => {
                        if (!dirInfo.Name.TryGetAttributeValue(ShokoSeriesId.Name, out var seriesId) || !int.TryParse(seriesId, out _))
                            return Array.Empty<BaseItem>();

                        var season = ApiManager.GetSeasonInfoForSeries(seriesId)
                            .ConfigureAwait(false)
                            .GetAwaiter()
                            .GetResult();
                        if (season == null)
                            return Array.Empty<BaseItem>();

                        if (createMovies && season.Type == SeriesType.Movie) {
                            return FileSystem.GetFiles(dirInfo.FullName)
                                .AsParallel()
                                .Select(fileInfo => {
                                    // Only allow the video files, since the subtitle files also have the ids set.
                                    if (!_namingOptions.VideoFileExtensions.Contains(Path.GetExtension(fileInfo.Name)))
                                        return null;

                                    if (!fileInfo.Name.TryGetAttributeValue(ShokoFileId.Name, out var fileId) || !int.TryParse(fileId, out _))
                                        return null;

                                    // This will hopefully just re-use the pre-cached entries from the cache, but it may
                                    // also get it from remote if the cache was emptied for whatever reason.
                                    var file = ApiManager.GetFileInfo(fileId, seriesId)
                                        .ConfigureAwait(false)
                                        .GetAwaiter()
                                        .GetResult();

                                    // Abort if the file was not recognised.
                                    if (file == null || file.ExtraType != null)
                                        return null;

                                    return new Movie() {
                                        Path = fileInfo.FullName,
                                    } as BaseItem;
                                })
                                .ToArray();
                        }

                        return new BaseItem[1] {
                            new TvSeries() {
                                Path = dirInfo.FullName,
                            },
                        };
                    })
                    .OfType<BaseItem>()
                    .ToList();

                // TODO: uncomment the code snippet once the PR is in stable JF.
                // return new() { Items = items, ExtraFiles = new() };

                // TODO: Remove these two hacks once we have proper support for adding multiple series at once.
                if (items.Where(i => i is Movie).ToList().Count == 0 && items.Count > 0) {
                    fileInfoList.Clear();
                    fileInfoList.AddRange(items.Select(s => FileSystem.GetFileSystemInfo(s.Path)));
                }

                return new() { Items = items.Where(i => i is Movie).ToList(), ExtraFiles = items.OfType<TvSeries>().Select(s => FileSystem.GetFileSystemInfo(s.Path)).ToList() };
            }

            // TODO: Redirect to the base item in the VFS if needed.

            return null;
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
            throw;
        }
    }

    #endregion
}