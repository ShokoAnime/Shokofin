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
using Shokofin.Utils;

using File = System.IO.File;
using TvSeries = MediaBrowser.Controller.Entities.TV.Series;

#nullable enable
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
            var vfsPath = ShokoAPIManager.GetVirtualRootForMediaFolder(folder);
            if (Directory.Exists(vfsPath)) {
                Logger.LogInformation("Removing VFS directory for folder at {Path}", folder.Path);
                Directory.Delete(vfsPath, true);
                Logger.LogInformation("Removed VFS directory for folder at {Path}", folder.Path);
            }
        }
    }

    #endregion

    #region Generate Structure

    private async Task<string?> GenerateStructureForFolder(Folder mediaFolder, string folderPath)
    {
        // Return early if we've already generated the structure from the import folder itself.
        if (DataCache.TryGetValue<string?>(mediaFolder.Path, out var vfsPath))
            return vfsPath;

        return await DataCache.GetOrCreateAsync(folderPath, async (cachedEntry) => {
            Logger.LogInformation("Looking for match for folder at {Path}.", folderPath);

            // Check if we should introduce the VFS for the media folder.
            var allPaths = FileSystem.GetFilePaths(folderPath, true)
                .Where(path => _namingOptions.VideoFileExtensions.Contains(Path.GetExtension(path)))
                .Take(100)
                .ToList();
            int importFolderId = 0;
            var attempts = 0;
            string importFolderSubPath = string.Empty;
            foreach (var path in allPaths) {
                attempts++;
                var partialPath = path[mediaFolder.Path.Length..];
                var partialFolderPath = path[folderPath.Length..];
                var files = ApiClient.GetFileByPath(partialPath)
                    .GetAwaiter()
                    .GetResult();
                var file = files.FirstOrDefault();
                if (file == null)
                    continue;

                var fileId = file.Id.ToString();
                var fileLocations = file.Locations
                    .Where(location => location.Path.EndsWith(partialFolderPath))
                    .ToList();
                if (fileLocations.Count == 0)
                    continue;

                var fileLocation = fileLocations[0];
                importFolderId = fileLocation.ImportFolderId;
                importFolderSubPath = fileLocation.Path[..^partialFolderPath.Length];
                break;
            }

            if (importFolderId == 0) {
                Logger.LogWarning("Failed to find a match for folder at {Path} after {Amount} attempts.", folderPath, allPaths.Count);

                cachedEntry.AbsoluteExpirationRelativeToNow = DefaultTTL;
                return null;
            }

            Logger.LogInformation("Found a match for folder at {Path} (ImportFolder={FolderId},RelativePath={RelativePath},MediaLibrary={Path},Attempts={Attempts})", folderPath, importFolderId, importFolderSubPath, mediaFolder.Path, attempts);

            vfsPath = ShokoAPIManager.GetVirtualRootForMediaFolder(mediaFolder);
            var allFiles = await GetImportFolderFiles(importFolderId, importFolderSubPath, folderPath);
            await GenerateSymbolicLinks(mediaFolder, allFiles);

            cachedEntry.AbsoluteExpirationRelativeToNow = DefaultTTL;
            return vfsPath;
        });
    }

    private async Task<IReadOnlyList<(string sourceLocation, string fileId, string seriesId, string[] episodeIds)>> GetImportFolderFiles(int importFolderId, string importFolderSubPath, string mediaFolderPath)
    {
        Logger.LogDebug("Looking up recognised files for media folder… (ImportFolder={FolderId},RelativePath={RelativePath})", importFolderId, importFolderSubPath);
        var start = DateTime.UtcNow;
        var allFilesForImportFolder = (await ApiClient.GetFilesForImportFolder(importFolderId, importFolderSubPath))
            .AsParallel()
            .SelectMany(file =>
            {
                var location = file.Locations
                    .Where(location => location.ImportFolderId == importFolderId && (importFolderSubPath.Length == 0 || location.Path.StartsWith(importFolderSubPath)))
                    .FirstOrDefault();
                if (location == null || file.CrossReferences.Count == 0)
                    return Array.Empty<(string sourceLocation, string fileId, string seriesId, string[] episodeIds)>();

                var sourceLocation = Path.Join(mediaFolderPath, location.Path[importFolderSubPath.Length..]);
                return file.CrossReferences
                    .Select(xref => (sourceLocation, fileId: file.Id.ToString(), seriesId: xref.Series.Shoko.ToString(), episodeIds: xref.Episodes.Select(e => e.Shoko.ToString()).ToArray()));
            })
            .Where(tuple => !string.IsNullOrEmpty(tuple.sourceLocation))
            .ToList();
        var timeSpent = DateTime.UtcNow - start;
        Logger.LogDebug(
            "Found ≤{FileCount} files to potentially use within media folder at {Path} in {TimeSpan} (ImportFolder={FolderId},RelativePath={RelativePath})",
            allFilesForImportFolder.Count,
            mediaFolderPath,
            timeSpent,
            importFolderId,
            importFolderSubPath
        );
        return allFilesForImportFolder;
    }

    private async Task GenerateSymbolicLinks(Folder mediaFolder, IReadOnlyList<(string sourceLocation, string fileId, string seriesId, string[] episodeIds)> files)
    {
        Logger.LogInformation("Creating structure for ≤{FileCount} files to potentially use within media folder at {Path}", files.Count, mediaFolder.Path);

        var start = DateTime.UtcNow;
        var skipped = 0;
        var vfsPath = ShokoAPIManager.GetVirtualRootForMediaFolder(mediaFolder);
        var collectionType = LibraryManager.GetInheritedContentType(mediaFolder);
        var allPathsForVFS = new ConcurrentBag<(string sourceLocation, string symbolicLink)>();
        var semaphore = new SemaphoreSlim(Plugin.Instance.Configuration.VirtualFileSystemThreads);
        await Task.WhenAll(files
            .Select(async (tuple) => {
                await semaphore.WaitAsync();

                try {
                    // Skip any source files we that we cannot find.
                    if (!File.Exists(tuple.sourceLocation))
                        return;

                    var (sourceLocation, symbolicLinks) = await GenerateLocationsForFile(vfsPath, collectionType, tuple.sourceLocation, tuple.fileId, tuple.seriesId, tuple.episodeIds);
                    // Skip any source files we weren't meant to have in the library.
                    if (string.IsNullOrEmpty(sourceLocation))
                        return;

                    var sourcePrefix = Path.GetFileNameWithoutExtension(sourceLocation);
                    var sourcePrefixLength = sourceLocation.Length - Path.GetExtension(sourceLocation).Length;
                    var subtitleLinks = FindSubtitlesForPath(sourceLocation);
                    foreach (var symbolicLink in symbolicLinks) {
                        if (File.Exists(symbolicLink)) {
                            skipped++;
                            allPathsForVFS.Add((sourceLocation, symbolicLink));
                            return;
                        }

                        var symbolicDirectory = Path.GetDirectoryName(symbolicLink)!;
                        if (!Directory.Exists(symbolicDirectory))
                            Directory.CreateDirectory(symbolicDirectory);

                        allPathsForVFS.Add((sourceLocation, symbolicLink));
                        File.CreateSymbolicLink(symbolicLink, sourceLocation);

                        if (subtitleLinks.Count > 0) {
                            var symbolicName = Path.GetFileNameWithoutExtension(symbolicLink);
                            foreach (var subtitleSource in subtitleLinks) {
                                var extName = subtitleSource[sourcePrefixLength..];
                                var subtitleLink = Path.Combine(symbolicDirectory, symbolicName + extName);
                                allPathsForVFS.Add((subtitleSource, subtitleLink));
                                File.CreateSymbolicLink(subtitleLink, subtitleSource);
                            }
                        }
                    }
                }
                finally {
                    semaphore.Release();
                }
            })
            .ToList());

        var toBeRemoved = FileSystem.GetFilePaths(ShokoAPIManager.GetVirtualRootForMediaFolder(mediaFolder), true)
            .Where(path => _namingOptions.VideoFileExtensions.Contains(Path.GetExtension(path)))
            .Except(allPathsForVFS.Select(tuple => tuple.symbolicLink).ToHashSet())
            .ToList();
        foreach (var symbolicLink in toBeRemoved) {
            var subtitleLinks = FindSubtitlesForPath(symbolicLink);

            File.Delete(symbolicLink);

            foreach (var subtitleLink in subtitleLinks)
                File.Delete(symbolicLink);

            CleanupDirectoryStructure(symbolicLink);
        }

        var timeSpent = DateTime.UtcNow - start;
        Logger.LogInformation(
            "Created {CreatedCount}, skipped {SkippedCount}, and removed {RemovedCount} symbolic links for media folder at {Path} in {TimeSpan}",
            allPathsForVFS.Count - skipped,
            skipped,
            toBeRemoved.Count,
            mediaFolder.Path,
            timeSpent
        );
    }

    private async Task<(string sourceLocation, string[] symbolicLinks)> GenerateLocationsForFile(string vfsPath, string? collectionType, string sourceLocation, string fileId, string seriesId, string[] episodeIds)
    {
        var season = await ApiManager.GetSeasonInfoForSeries(seriesId);
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

        var show = await ApiManager.GetShowInfoForSeries(seriesId);
        if (show == null)
            return (sourceLocation: string.Empty, symbolicLinks: Array.Empty<string>());

        var file = await ApiManager.GetFileInfo(fileId, seriesId);
        var episode = file?.EpisodeList.FirstOrDefault();
        if (file == null || episode == null)
            return (sourceLocation: string.Empty, symbolicLinks: Array.Empty<string>());

        if (season == null || episode == null)
            return (sourceLocation: string.Empty, symbolicLinks: Array.Empty<string>());

        var isSpecial = episode.IsSpecial;
        var episodeNumber = Ordering.GetEpisodeNumber(show, season, episode);
        var seasonNumber = Ordering.GetSeasonNumber(show, season, episode);
        var showName = show.DefaultSeason.AniDB.Title?.ReplaceInvalidPathCharacters();
        if (string.IsNullOrEmpty(showName))
            showName = $"Shoko Series {show.Id}";
        else if (show.DefaultSeason.AniDB.AirDate.HasValue)
            showName += $" ({show.DefaultSeason.AniDB.AirDate.Value.Year})";

        var folders = new List<string>();
        var episodeName = (episode.AniDB.Titles.FirstOrDefault(t => t.LanguageCode == "en")?.Value ?? $"Episode {episode.AniDB.Type} {episodeNumber}").ReplaceInvalidPathCharacters();
        var extrasFolder = file.ExtraType switch {
            ExtraType.BehindTheScenes => "behind the scenes",
            ExtraType.Clip => "clips",
            ExtraType.DeletedScene => "deleted scene",
            ExtraType.Interview => "interviews",
            ExtraType.Sample => "samples",
            ExtraType.Scene => "scenes",
            ExtraType.ThemeSong => "theme-music",
            ExtraType.ThemeVideo => "backdrops",
            ExtraType.Trailer => "trailers",
            ExtraType.Unknown => "others",
            null => null,
            _ => "extras",
        };

        if (isMovieSeason && collectionType != CollectionType.TvShows) {
            if (!string.IsNullOrEmpty(extrasFolder)) {
                foreach (var episodeInfo in season.EpisodeList)
                    folders.Add(Path.Combine(vfsPath, $"{showName} [shoko-series-{show.Id}] [shoko-episode-{episodeInfo.Id}]", extrasFolder));
            }
            else {
                folders.Add(Path.Combine(vfsPath, $"{showName} [shoko-series-{show.Id}] [shoko-episode-{episode.Id}]"));
                episodeName = "Movie";
            }
        }
        else {
            var seasonName = $"Season {(isSpecial ? 0 : seasonNumber).ToString().PadLeft(2, '0')}";
            if (!string.IsNullOrEmpty(extrasFolder)) {
                folders.Add(Path.Combine(vfsPath, $"{showName} [shoko-series-{show.Id}]", extrasFolder));
                folders.Add(Path.Combine(vfsPath, $"{showName} [shoko-series-{show.Id}]", seasonName, extrasFolder));
            }
            else {
                folders.Add(Path.Combine(vfsPath, $"{showName} [shoko-series-{show.Id}]", seasonName));
                episodeName = $"{showName} S{(isSpecial ? 0 : seasonNumber).ToString().PadLeft(2, '0')}E{episodeNumber}";
            }
        }

        var fileName = $"{episodeName} [shoko-series-{seriesId}] [shoko-file-{fileId}]{Path.GetExtension(sourceLocation)}";
        var symbolicLinks = folders
            .Select(folderPath => Path.Combine(folderPath, fileName))
            .ToArray();

        foreach (var symbolicLink in symbolicLinks)
            ApiManager.AddFileLookupIds(symbolicLink, fileId, seriesId, episodeIds);
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
        if (parent == null || fileInfo == null)
            return false;

        var root = LibraryManager.RootFolder;
        if (root == null || parent == root || parent.ParentId == root.Id)
            return false;

        try {
            // Assume anything within the VFS is already okay.
            if (fileInfo.FullName.StartsWith(Plugin.Instance.VirtualRoot))
                return false;

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

            var shouldIgnore = Plugin.Instance.Configuration.LibraryFilteringMode ?? Plugin.Instance.Configuration.VirtualFileSystem || isSoleProvider;
            var collectionType = LibraryManager.GetInheritedContentType(mediaFolder);
            if (fileInfo.IsDirectory)
                return await ScanDirectory(partialPath, fullPath, collectionType, shouldIgnore);
            else
                return await ScanFile(partialPath, fullPath, shouldIgnore);
        }
        catch (Exception ex) {
            if (!(ex is System.Net.Http.HttpRequestException && ex.Message.Contains("Connection refused")))
            {
                Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
            }
            return false;
        }
    }

    private async Task<bool> ScanDirectory(string partialPath, string fullPath, string? collectionType, bool shouldIgnore)
    {
        var season = await ApiManager.GetSeasonInfoByPath(fullPath);

        // We inform/warn here since we enabled the provider in our library, but we can't find a match for the given folder path.
        if (season == null) {
            // If we're in strict mode, then check the sub-directories if we have a <Show>/<Season>/<Episodes> structure.
            if (shouldIgnore && partialPath[1..].Split(Path.DirectorySeparatorChar).Length == 1) {
                try {
                    var entries = FileSystem.GetDirectories(fullPath, false).ToList();
                    Logger.LogDebug("Unable to find shoko series for {Path}, trying {DirCount} sub-directories.", partialPath, entries.Count);
                    foreach (var entry in entries) {
                        season = await ApiManager.GetSeasonInfoByPath(entry.FullName);
                        if (season != null)
                        {
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

        var show = await ApiManager.GetShowInfoForSeries(season.Id)!;
        if (!string.IsNullOrEmpty(show!.GroupId))
            Logger.LogInformation("Found shoko group {GroupName} (Series={SeriesId},Group={GroupId})", show.Name, season.Id, show.GroupId);
        else
            Logger.LogInformation("Found shoko series {SeriesName} (Series={SeriesId})", season.Shoko.Name, season.Id);

        return false;
    }

    private async Task<bool> ScanFile(string partialPath, string fullPath, bool shouldIgnore)
    {
        var (file, season, _) = await ApiManager.GetFileInfoByPath(fullPath);

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
        if (!Plugin.Instance.Configuration.VirtualFileSystem)
            return null;

        if (!(collectionType == CollectionType.TvShows || collectionType == CollectionType.Movies || collectionType == null) || parent == null || fileInfo == null)
            return null;

        var root = LibraryManager.RootFolder;
        if (root == null || parent == root)
            return null;

        try {
            if (!Lookup.IsEnabledForItem(parent))
                return null;

            var fullPath = fileInfo.FullName;
            var (mediaFolder, partialPath) = ApiManager.FindMediaFolder(fullPath, parent, root);
            if (mediaFolder == root)
                return null;

            // We're most likely already within the VFS, so abort here.
            if (!fullPath.StartsWith(Plugin.Instance.VirtualRoot))
                return null;

            var searchPath = mediaFolder.Path != parent.Path
                ? Path.Combine(mediaFolder.Path, parent.Path[(mediaFolder.Path.Length + 1)..].Split(Path.DirectorySeparatorChar).Skip(1).Join(Path.DirectorySeparatorChar))
                : mediaFolder.Path;
            var vfsPath = await GenerateStructureForFolder(mediaFolder, searchPath);
            if (string.IsNullOrEmpty(vfsPath))
                return null;

            if (parent.Id == mediaFolder.Id && fileInfo.IsDirectory) {
                var seriesSegment = fileInfo.Name.Split('[').Last().Split(']').First();
                if (!int.TryParse(seriesSegment.Split('-').LastOrDefault(), out var seriesId))
                    return null;

                return new TvSeries()
                {
                    Path = fileInfo.FullName,
                };
            }

            // TODO: Redirect to the base item in the VFS if needed.

            return null;
        }
        catch (Exception ex) {
            if (!(ex is System.Net.Http.HttpRequestException && ex.Message.Contains("Connection refused")))
            {
                Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
            }
            return null;
        }
    }

    public async Task<MultiItemResolverResult?> ResolveMultiple(Folder? parent, string? collectionType, List<FileSystemMetadata> fileInfoList)
    {
        if (!Plugin.Instance.Configuration.VirtualFileSystem)
            return null;

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
                var vfsPath = await GenerateStructureForFolder(parent, parent.Path);
                if (string.IsNullOrEmpty(vfsPath))
                    return null;

                var createMovies = collectionType == CollectionType.Movies || (collectionType == null && Plugin.Instance.Configuration.SeparateMovies);
                var items = FileSystem.GetDirectories(vfsPath)
                    .AsParallel()
                    .SelectMany(dirInfo => {
                        var seriesSegment = dirInfo.Name.Split('[').Last().Split(']').First();
                        if (!int.TryParse(seriesSegment.Split('-').LastOrDefault(), out var seriesId))
                            return Array.Empty<BaseItem>();

                        var season = ApiManager.GetSeasonInfoForSeries(seriesId.ToString())
                            .GetAwaiter()
                            .GetResult();
                        if (season == null)
                            return Array.Empty<BaseItem>();

                        if (createMovies && season.Type == SeriesType.Movie) {
                            return FileSystem.GetFiles(dirInfo.FullName)
                                .AsParallel()
                                .Select(fileInfo => {
                                    if (!int.TryParse(Path.GetFileNameWithoutExtension(fileInfo.Name).Split('[').LastOrDefault()?.Split(']').FirstOrDefault()?.Split('-').LastOrDefault(), out var fileId))
                                        return null;

                                    // This will hopefully just re-use the pre-cached entries from the cache, but it may
                                    // also get it from remote if the cache was emptied for whatever reason.
                                    var file = ApiManager.GetFileInfo(fileId.ToString(), seriesId.ToString())
                                        .GetAwaiter()
                                        .GetResult();

                                    // Abort if the file was not recognised.
                                    if (file == null || file.ExtraType != null)
                                        return null;

                                    return new Movie()
                                    {
                                        Path = fileInfo.FullName,
                                        ProviderIds = new() {
                                            { "Shoko File", fileId.ToString() },
                                        }
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

            return null;
        }
        catch (Exception ex) {
            if (!(ex is System.Net.Http.HttpRequestException && ex.Message.Contains("Connection refused")))
            {
                Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
            }
            return null;
        }
    }

    #endregion
}