using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Emby.Naming.Common;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Models;
using Shokofin.Utils;

using ApiFile = Shokofin.API.Models.File;
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

    private readonly IMemoryCache DataCache = new MemoryCache(new MemoryCacheOptions() {
        ExpirationScanFrequency = TimeSpan.FromMinutes(50),
    });

    private static readonly TimeSpan DefaultTTL = TimeSpan.FromMinutes(60);

    public ShokoResolveManager(ShokoAPIManager apiManager, ShokoAPIClient apiClient, IIdLookup lookup, ILibraryManager libraryManager, IFileSystem fileSystem, ILogger<ShokoResolveManager> logger, NamingOptions namingOptions)
    {
        ApiManager = apiManager;
        ApiClient = apiClient;
        Lookup = lookup;
        LibraryManager = libraryManager;
        FileSystem = fileSystem;
        Logger = logger;
        _namingOptions = namingOptions;
        LibraryManager.ItemRemoved += OnLibraryManagerItemRemoved;
    }

    ~ShokoResolveManager()
    {
        LibraryManager.ItemRemoved -= OnLibraryManagerItemRemoved;
        DataCache.Dispose();
    }

    #region Changes Tracking

    private void OnLibraryManagerItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        var root = LibraryManager.RootFolder;
        if (e.Item != null && root != null && e.Item != root && e.Item is Folder folder && folder.ParentId == Guid.Empty  && !string.IsNullOrEmpty(folder.Path) && !folder.Path.StartsWith(root.Path)) {
            DataCache.Remove(folder.Id.ToString());
            var vfsPath = ShokoAPIManager.GetVirtualRootForMediaFolder(folder);
            if (Directory.Exists(vfsPath)) {
                Logger.LogDebug("Removing VFS directory for folder");
                Directory.Delete(vfsPath, true);
            }
        }
    }

    #endregion

    #region Generate Structure

    private async Task<IReadOnlyList<(string sourceLocation, string fileId, string seriesId, string[] episodeIds)>> GetImportFolderFiles(int importFolderId, string importFolderSubPath, string mediaFolderPath)
    {
        Logger.LogDebug("Looking for recognised files within media folder… (ImportFolder={FolderId},RelativePath={RelativePath})", importFolderId, importFolderSubPath);
        var allFilesForImportFolder = (await ApiClient.GetFilesForImportFolder(importFolderId))
            .AsParallel()
            .SelectMany(file =>
            {
                var location = file.Locations
                    .Where(location => location.ImportFolderId == importFolderId && (importFolderSubPath.Length == 0 || location.Path.StartsWith(importFolderSubPath)))
                    .FirstOrDefault();
                if (location == null || file.CrossReferences.Count == 0)
                    return Array.Empty<(string sourceLocation, string fileId, string seriesId, string[] episodeIds)>();

                var sourceLocation = Path.Join(mediaFolderPath, location.Path[importFolderSubPath.Length..]);
                if (!File.Exists(sourceLocation))
                    return Array.Empty<(string sourceLocation, string fileId, string seriesId, string[] episodeIds)>();

                return file.CrossReferences
                    .Select(xref => (sourceLocation, fileId: file.Id.ToString(), seriesId: xref.Series.Shoko.ToString(), episodeIds: xref.Episodes.Select(e => e.Shoko.ToString()).ToArray()));
            })
            .Where(tuple => !string.IsNullOrEmpty(tuple.sourceLocation))
            .ToList();
        Logger.LogDebug("Found {FileCount} files to use within media folder at {Path} (ImportFolder={FolderId},RelativePath={RelativePath})", allFilesForImportFolder.Count, mediaFolderPath, importFolderId, importFolderSubPath);
        return allFilesForImportFolder;
    }

    private async Task<string?> GenerateStructureForFolder(Folder mediaFolder, string folderPath)
    {
        if (DataCache.TryGetValue<string?>(folderPath, out var vfsPath) || DataCache.TryGetValue(mediaFolder.Path, out vfsPath))
            return vfsPath;

        Logger.LogDebug("Looking for match for folder at {Path}.", folderPath);

        // Check if we should introduce the VFS for the media folder.
        var allPaths = FileSystem.GetFilePaths(folderPath, true)
            .Where(path => _namingOptions.VideoFileExtensions.Contains(Path.GetExtension(path)))
            .Take(100)
            .ToList();
        int importFolderId = 0;
        string importFolderSubPath = string.Empty;
        foreach (var path in allPaths) {
            var partialPath = path[mediaFolder.Path.Length..];
            var partialFolderPath = path[folderPath.Length..];
            var file = ApiClient.GetFileByPath(partialPath)
                .GetAwaiter()
                .GetResult()
                .FirstOrDefault();
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

        if (importFolderId != 0) {
            Logger.LogDebug("Failed to find a match for folder at {Path} after {Amount} attempts.", folderPath, allPaths.Count);

            DataCache.Set<string?>(folderPath, null, DefaultTTL);
            return null;
        }

        Logger.LogDebug("Found a match for folder at {Path} (ImportFolder={FolderId},RelativePath={RelativePath},MediaLibrary={Path})", folderPath, importFolderId, importFolderSubPath, mediaFolder.Path);

        vfsPath = ShokoAPIManager.GetVirtualRootForMediaFolder(mediaFolder);
        DataCache.Set(folderPath, vfsPath, DefaultTTL);
        var allFiles = await GetImportFolderFiles(importFolderId, importFolderSubPath, folderPath);
        await GenerateSymbolicLinks(mediaFolder, allFiles);

        return vfsPath;
    }

    private async Task GenerateSymbolicLinks(Folder mediaFolder, IEnumerable<(string sourceLocation, string fileId, string seriesId, string[] episodeIds)> files)
    {
        var allPathsForVFS = (await Task.WhenAll(files.AsParallel().Select((tuple) => GenerateLocationForFile(mediaFolder, tuple.sourceLocation, tuple.fileId, tuple.seriesId, tuple.episodeIds)).ToList()))
            .Where(tuple => !string.IsNullOrEmpty(tuple.sourceLocation))
            .OrderBy(tuple => tuple.sourceLocation)
            .ThenBy(tuple => tuple.symbolicLink)
            .ToList();

        var skipped = 0;
        var created = 0;
        foreach (var (sourceLocation, symbolicLink) in allPathsForVFS) {
            if (File.Exists(symbolicLink)) {
                skipped++;
                continue;
            }

            var symbolicDirectory = Path.GetDirectoryName(symbolicLink);
            if (!string.IsNullOrEmpty(symbolicDirectory) && !Directory.Exists(symbolicDirectory))
                Directory.CreateDirectory(symbolicDirectory);

            created++;
            File.CreateSymbolicLink(symbolicLink, sourceLocation);

            // TODO: Check for subtitle files.
        }
        var vfsPath = ShokoAPIManager.GetVirtualRootForMediaFolder(mediaFolder);
        var toBeRemoved = FileSystem.GetFilePaths(vfsPath, true)
            .Where(path => _namingOptions.VideoFileExtensions.Contains(Path.GetExtension(path)))
            .Except(allPathsForVFS.Select(tuple => tuple.symbolicLink).ToHashSet())
            .ToList();
        foreach (var symbolicLink in toBeRemoved) {
            // TODO: Check for subtitle files.

            File.Delete(symbolicLink);
            var symbolicDirectory = Path.GetDirectoryName(symbolicLink);
            if (!string.IsNullOrEmpty(symbolicDirectory) && Directory.Exists(symbolicDirectory) && !Directory.EnumerateFileSystemEntries(symbolicDirectory).Any())
                Directory.Delete(symbolicDirectory);
        }

        Logger.LogDebug(
            "Created {CreatedCount}, skipped {SkippedCount}, and removed {RemovedCount} symbolic links for media folder at {Path}",
            created,
            skipped,
            toBeRemoved.Count,
            mediaFolder.Path
        );
    }

    private async Task<(string sourceLocation, string symbolicLink)> GenerateLocationForFile(Folder mediaFolder, string sourceLocation, string fileId, string seriesId, string[] episodeIds)
    {
        var vfsPath = ShokoAPIManager.GetVirtualRootForMediaFolder(mediaFolder);
        var collectionType = LibraryManager.GetInheritedContentType(mediaFolder);
        var filterType = Ordering.GetGroupFilterTypeForCollection(collectionType);
        var season = await ApiManager.GetSeasonInfoForSeries(seriesId);
        if (season == null)
            return (sourceLocation: string.Empty, symbolicLink: string.Empty);
        var isMovieSeason = season.AniDB.Type == SeriesType.Movie;
        switch (collectionType) {
            default: {
                if (isMovieSeason && collectionType == null)
                    goto case CollectionType.Movies;

                var show = await ApiManager.GetShowInfoForSeries(seriesId, filterType);
                if (show == null)
                    return (sourceLocation: string.Empty, symbolicLink: string.Empty);

                var file = await ApiManager.GetFileInfo(fileId, seriesId);
                var episode = file?.EpisodeList.FirstOrDefault();
                if (file == null || episode == null)
                    return (sourceLocation: string.Empty, symbolicLink: string.Empty);

                // In the off-chance that we accidentially ended up with two
                // instances of the season while fetching in parallel, then we're
                // switching to the correct reference of the season for the show
                // we're doing. Let's just hope we won't have to also need to switch
                // the episode…
                season = show.SeasonList.FirstOrDefault(s => s.Id == seriesId);
                episode = season?.RawEpisodeList.FirstOrDefault(e => e.Id == episode.Id);
                if (season == null || episode == null)
                    return (sourceLocation: string.Empty, symbolicLink: string.Empty);

                var defaultSeason = show.DefaultSeason ?? season;
                var episodeNumber = Ordering.GetEpisodeNumber(show, season, episode);
                var seasonNumber = Ordering.GetSeasonNumber(show, season, episode);
                var (_, _, _, isSpecial) = Ordering.GetSpecialPlacement(show, season, episode);

                var showName = defaultSeason.AniDB.Title?.ReplaceInvalidPathCharacters();
                if (string.IsNullOrEmpty(showName))
                    showName = $"Shoko Series {defaultSeason.Id}";
                if (defaultSeason.AniDB.AirDate.HasValue)
                    showName += $" ({defaultSeason.AniDB.AirDate.Value.Year})";

                var episodeName = $"{showName} S{(isSpecial ? 0 : seasonNumber).ToString().PadLeft(2, '0')}E{episodeNumber}";
                var paths = new List<string>()
                {
                    vfsPath,
                    $"{showName} [shoko-series-{defaultSeason.Id}]",
                    $"Season {(isSpecial ? 0 : seasonNumber).ToString().PadLeft(2, '0')}",
                };
                if (file.ExtraType != null)
                {
                    episodeName = episode.AniDB.Titles.FirstOrDefault(t => t.LanguageCode == "en")?.Value ?? $"Episode {episode.AniDB.Type} {episode.AniDB.EpisodeNumber}";
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
                        _ => "extras",
                    };
                    paths.Add(extrasFolder);
                }

                var fileName = $"{episodeName} [shoko-series-{seriesId}] [shoko-file-{fileId}{Path.GetExtension(sourceLocation)}]";
                paths.Add(fileName);
                var symbolicLink = Path.Combine(paths.ToArray());
                ApiManager.AddFileLookupIds(symbolicLink, fileId, seriesId, episodeIds);
                return (sourceLocation, symbolicLink);
            }
            case CollectionType.TvShows: {
                if (isMovieSeason && Plugin.Instance.Configuration.FilterOnLibraryTypes)
                    return (sourceLocation: string.Empty, symbolicLink: string.Empty);

                goto default;
            }
            case CollectionType.Movies: {
                if (!isMovieSeason)
                    return (sourceLocation: string.Empty, symbolicLink: string.Empty);

                var fileName = $"Movie File [shoko-series-{seriesId}] [shoko-file-{fileId}{Path.GetExtension(sourceLocation)}]";
                var symbolicLink = Path.Combine(vfsPath, $"Shoko Series {seriesId} [shoko-series-{seriesId}]", fileName);

                ApiManager.AddFileLookupIds(symbolicLink, fileId, seriesId, episodeIds);
                return (sourceLocation, symbolicLink);
            }
        }
    }

    #endregion

    #region Ignore Rule

    public bool ShouldFilterItem(Folder? parent, FileSystemMetadata fileInfo)
    {
        // Everything in the root folder is ignored by us.
        var root = LibraryManager.RootFolder;
        if (fileInfo == null || parent == null || root == null || parent == root || fileInfo.FullName.StartsWith(root.Path))
            return false;

        try {
            // Assume anything within the VFS is already okay.
            if (fileInfo.FullName.StartsWith(Plugin.Instance.VirtualRoot))
                return false;

            // Enable the scanner if we selected to use the Shoko provider for any metadata type on the current root folder.
            if (!Lookup.IsEnabledForItem(parent, out var isSoleProvider))
                return false;

            if (fileInfo.IsDirectory &&  Plugin.Instance.IgnoredFolders.Contains(Path.GetFileName(fileInfo.FullName).ToLowerInvariant())) {
                Logger.LogDebug("Excluded folder at path {Path}", fileInfo.FullName);
                return true;
            }

            if (!fileInfo.IsDirectory && Plugin.Instance.IgnoredFileExtensions.Contains(fileInfo.Extension.ToLowerInvariant())) {
                Logger.LogDebug("Skipped excluded file at path {Path}", fileInfo.FullName);
                return false;
            }

            var fullPath = fileInfo.FullName;
            var (mediaFolder, partialPath) = ApiManager.FindMediaFolder(fullPath, parent, root);

            var shouldIgnore = Plugin.Instance.Configuration.LibraryFilteringMode ?? Plugin.Instance.Configuration.EXPERIMENTAL_EnableResolver || isSoleProvider;
            var collectionType = LibraryManager.GetInheritedContentType(mediaFolder);
            var filterType = Ordering.GetGroupFilterTypeForCollection(collectionType);
            if (fileInfo.IsDirectory)
                return ScanDirectory(partialPath, fullPath, collectionType, filterType, shouldIgnore);
            else
                return ScanFile(partialPath, fullPath, filterType, shouldIgnore);
        }
        catch (Exception ex) {
            if (!(ex is System.Net.Http.HttpRequestException && ex.Message.Contains("Connection refused")))
            {
                Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
            }
            return false;
        }
    }

    private bool ScanDirectory(string partialPath, string fullPath, string? collectionType, Ordering.GroupFilterType filterType, bool shouldIgnore)
    {
        var season = ApiManager.GetSeasonInfoByPath(fullPath)
            .GetAwaiter()
            .GetResult();

        // We inform/warn here since we enabled the provider in our library, but we can't find a match for the given folder path.
        if (season == null) {
            // If we're in strict mode, then check the sub-directories if we have a <Show>/<Season>/<Episodes> structure.
            if (shouldIgnore && partialPath[1..].Split(Path.DirectorySeparatorChar).Length == 1) {
                try {
                    var entries = FileSystem.GetDirectories(fullPath, false).ToList();
                    Logger.LogDebug("Unable to find shoko series for {Path}, trying {DirCount} sub-directories.", partialPath, entries.Count);
                    foreach (var entry in entries) {
                        season = ApiManager.GetSeasonInfoByPath(entry.FullName)
                            .GetAwaiter()
                            .GetResult();
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
        var isMovieSeason = season.AniDB.Type == SeriesType.Movie;
        switch (collectionType) {
            case CollectionType.TvShows:
                if (isMovieSeason && Plugin.Instance.Configuration.FilterOnLibraryTypes) {
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

        var show = ApiManager.GetShowInfoForSeries(season.Id, filterType)
                .GetAwaiter()
                .GetResult()!;

        if (!string.IsNullOrEmpty(show.Id))
            Logger.LogInformation("Found shoko group {GroupName} (Series={SeriesId},Group={GroupId})", show.Name, season.Id, show.Id);
        else
            Logger.LogInformation("Found shoko series {SeriesName} (Series={SeriesId})", season.Shoko.Name, season.Id);

        return false;
    }

    private bool ScanFile(string partialPath, string fullPath, Ordering.GroupFilterType filterType, bool shouldIgnore)
    {
        var (file, season, _) = ApiManager.GetFileInfoByPath(fullPath, filterType)
            .GetAwaiter()
            .GetResult();

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

    public BaseItem? ResolveSingle(Folder? parent, string? collectionType, FileSystemMetadata fileInfo)
    {
        // Disable resolver.
        if (!Plugin.Instance.Configuration.EXPERIMENTAL_EnableResolver)
            return null;

        // Everything in the root folder is ignored by us.
        var root = LibraryManager.RootFolder;
        if (!(collectionType == CollectionType.TvShows || collectionType == CollectionType.Movies || collectionType == null) || fileInfo == null || parent == null || root == null || parent == root || fileInfo.FullName.StartsWith(root.Path))
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

            var searchPath = Path.Combine(mediaFolder.Path, parent.Path[(mediaFolder.Path.Length + 1)..].Split(Path.DirectorySeparatorChar).Skip(1).Join(Path.DirectorySeparatorChar));
            var vfsPath = GenerateStructureForFolder(mediaFolder, searchPath)
                .GetAwaiter()
                .GetResult();
            if (string.IsNullOrEmpty(vfsPath))
                return null;

            if (parent.Id == mediaFolder.Id && fileInfo.IsDirectory) {
                if (!int.TryParse(fileInfo.Name.Split('-').LastOrDefault(), out var seriesId))
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

    public MultiItemResolverResult? ResolveMultiple(Folder? parent, string? collectionType, List<FileSystemMetadata> fileInfoList)
    {
        // Disable resolver.
        if (!Plugin.Instance.Configuration.EXPERIMENTAL_EnableResolver)
            return null;

        var root = LibraryManager.RootFolder;
        if (!(collectionType == CollectionType.TvShows || collectionType == CollectionType.Movies || collectionType == null) || root == null || parent == null || parent == root)
            return null;

        try {
            if (!Lookup.IsEnabledForItem(parent))
                return null;

            // Redirect children of a VFS managed media folder to the VFS.
            if (parent.GetParent() == root) {
                var vfsPath = GenerateStructureForFolder(parent, parent.Path)
                    .GetAwaiter()
                    .GetResult();
                if (string.IsNullOrEmpty(vfsPath))
                    return null;

                var filterType = Ordering.GetGroupFilterTypeForCollection(collectionType);
                var items = FileSystem.GetDirectories(vfsPath)
                    .AsParallel()
                    .SelectMany(dirInfo => {
                        if (!int.TryParse(dirInfo.Name.Split('-').LastOrDefault(), out var seriesId))
                            return Array.Empty<BaseItem>();

                        var season = ApiManager.GetSeasonInfoForSeries(seriesId.ToString())
                            .GetAwaiter()
                            .GetResult();
                        if (season == null)
                            return Array.Empty<BaseItem>();

                        if ((collectionType == CollectionType.Movies || collectionType == null) && season.AniDB.Type == SeriesType.Movie) {
                            return FileSystem.GetFiles(dirInfo.FullName)
                                .AsParallel()
                                .Select(fileInfo => {
                                    if (!int.TryParse(Path.GetFileNameWithoutExtension(fileInfo.Name).Split('[').LastOrDefault()?.Split(']').FirstOrDefault()?.Split('-').LastOrDefault(), out var fileId))
                                        return null;

                                    // This will hopefully just re-use the pre-cached entries from the cache, but it may
                                    // also get it from remote if the cache was empty for whatever reason.
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