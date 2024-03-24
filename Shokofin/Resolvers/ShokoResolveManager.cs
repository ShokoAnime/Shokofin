using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    private bool GenerateFullStructureForMediaFolder(Folder mediaFolder)
    {
        if (DataCache.TryGetValue<bool>(mediaFolder.Id.ToString(), out var isVFS))
            return isVFS;

        Logger.LogDebug("Looking for match for media folder at {Path}", mediaFolder.Path);

        // check if we should introduce the VFS for the folder
        var allPaths = FileSystem.GetFilePaths(mediaFolder.Path, true)
            .Where(path => _namingOptions.VideoFileExtensions.Contains(Path.GetExtension(path)))
            .Select(path => path[mediaFolder.Path.Length..]);
        ApiFile? file = null;
        int importFolderId = 0;
        string partialPathSegment = string.Empty;
        foreach (var partialPath in allPaths) {
            var files = ApiClient.GetFileByPath(partialPath)
                .GetAwaiter()
                .GetResult();
            file = files.FirstOrDefault();
            if (file == null)
                continue;

            var fileId = file.Id.ToString();
            var fileLocations = file.Locations
                .Where(location => location.Path.EndsWith(partialPath))
                .ToList();
            if (fileLocations.Count == 0)
                continue;

            var fileLocation = fileLocations[0];
            importFolderId = fileLocation.ImportFolderId;
            partialPathSegment = fileLocation.Path[..^partialPath.Length];
            break;
        }

        DataCache.Set(mediaFolder.Id.ToString(), file != null, DefaultTTL);
        if (file == null)
            return false;

        Logger.LogDebug("Found a match for media library at {Path} (ImportFolder={FolderId},RelativePath={RelativePath})", mediaFolder.Path, importFolderId, partialPathSegment);
        var filterType = !Plugin.Instance.Configuration.FilterOnLibraryTypes ? (
            Ordering.GroupFilterType.Default
        ) : (
            LibraryManager.GetInheritedContentType(mediaFolder) switch {
                "movies" => Ordering.GroupFilterType.Movies,
                _ => Ordering.GroupFilterType.Others,
            }
        );
        Logger.LogDebug("Looking for files within import folder… (ImportFolder={FolderId},RelativePath={RelativePath})", importFolderId, partialPathSegment);
        var vfsPath = ShokoAPIManager.GetVirtualRootForMediaFolder(mediaFolder);
        var allFilesForImportFolder = ApiClient.GetFilesForImportFolder(importFolderId)
            .GetAwaiter()
            .GetResult()
            .AsParallel()
            .Where(file => file.Locations.Any(location => location.ImportFolderId == importFolderId && (partialPathSegment.Length == 0 || location.Path.StartsWith(partialPathSegment))))
            .SelectMany(file => {
                var location = file.Locations
                    .Where(location => location.ImportFolderId == importFolderId && (partialPathSegment.Length == 0 || location.Path.StartsWith(partialPathSegment)))
                    .First();

                var sourceLocation = Path.Join(mediaFolder.Path, location.Path[partialPathSegment.Length..]);
                if (!File.Exists(sourceLocation))
                    return Array.Empty<(string sourceLocation, string symbolicLink)>();
                return file.CrossReferences
                    .AsParallel()
                    .Select(xref => {
                        var season = ApiManager.GetSeasonInfoForSeries(xref.Series.Shoko.ToString())
                            .GetAwaiter()
                            .GetResult();
                        if (season == null)
                            return (sourceLocation: string.Empty, symbolicLink: string.Empty);
                        var fileName = $"shoko-file-{file.Id}{Path.GetExtension(sourceLocation)}";
                        var showFolder = $"shoko-series-{season.Id}";
                        var isGrouped = Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup;
                        switch (filterType) {
                            case Ordering.GroupFilterType.Movies: {
                                var isMovieSeason = season.AniDB.Type == SeriesType.Movie;
                                if (!isMovieSeason)
                                    return (sourceLocation: string.Empty, symbolicLink: string.Empty);

                                return (sourceLocation, symbolicLink: Path.Combine(vfsPath, showFolder, fileName));
                            }
                            case Ordering.GroupFilterType.Others: {
                                var isMovieSeason = season.AniDB.Type == SeriesType.Movie;
                                if (isMovieSeason)
                                    return (sourceLocation: string.Empty, symbolicLink: string.Empty);

                                goto default;
                            }
                            default:
                            case Ordering.GroupFilterType.Default: {
                                var isMovieSeason = season.AniDB.Type == SeriesType.Movie;
                                if (isMovieSeason)
                                    return (sourceLocation, symbolicLink: Path.Combine(vfsPath, showFolder, fileName));

                                var fileInfo = ApiManager.GetFileInfo(file.Id.ToString(), season.Id)
                                    .GetAwaiter()
                                    .GetResult();
                                if (fileInfo == null)
                                    return (sourceLocation: string.Empty, symbolicLink: string.Empty);

                                var show = ApiManager.GetShowInfoForSeries(xref.Series.Shoko.ToString(), filterType)
                                    .GetAwaiter()
                                    .GetResult();
                                season = show?.SeasonList.First(s => s.Id == season.Id);
                                if (show == null || season == null)
                                    return (sourceLocation: string.Empty, symbolicLink: string.Empty);

                                var episode = fileInfo.EpisodeList.FirstOrDefault();
                                var seasonNumber = Ordering.GetSeasonNumber(show, season, episode);
                                var seasonFolder = $"Season {seasonNumber} [shoko-series-{season.Id}]";
                                showFolder = $"grouped-by-{show.DefaultSeason?.Id ?? xref.Series.Shoko.ToString()}";

                                return (sourceLocation, symbolicLink: Path.Combine(vfsPath, showFolder, seasonFolder, fileName));
                            }
                        }
                    })
                    .ToArray();
            })
            .Where(tuple => !string.IsNullOrEmpty(tuple.sourceLocation))
            .OrderBy(tuple => tuple.sourceLocation)
            .ThenBy(tuple => tuple.symbolicLink)
            .ToList();

        var skipped = 0;
        var created = 0;
        foreach (var (sourceLocation, symbolicLink) in allFilesForImportFolder) {
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
        var toBeRemoved = FileSystem.GetFilePaths(vfsPath, true)
            .Where(path => _namingOptions.VideoFileExtensions.Contains(Path.GetExtension(path)))
            .Except(allFilesForImportFolder.Select(tuple => tuple.symbolicLink).ToHashSet())
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

        return true;
    }

    #endregion

    #region Ignore Rule

    public bool ShouldFilterItem(Folder? parent, FileSystemMetadata fileInfo)
    {
        if (Plugin.Instance.Configuration.EXPERIMENTAL_EnableResolver)
            return false;

        // Everything in the root folder is ignored by us.
        var root = LibraryManager.RootFolder;
        if (fileInfo == null || parent == null || root == null || parent == root || fileInfo.FullName.StartsWith(root.Path))
            return false;

        try {
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
            var ordering = !Plugin.Instance.Configuration.FilterOnLibraryTypes ? (
                Ordering.GroupFilterType.Default
            ) : (
                LibraryManager.GetInheritedContentType(parent) switch {
                    "movies" => Ordering.GroupFilterType.Movies,
                    _ => Ordering.GroupFilterType.Others,
                }
            );
            if (fileInfo.IsDirectory)
                return ScanDirectory(partialPath, fullPath, ordering, shouldIgnore);
            else
                return ScanFile(partialPath, fullPath, ordering, shouldIgnore);
        }
        catch (System.Exception ex) {
            if (!(ex is System.Net.Http.HttpRequestException && ex.Message.Contains("Connection refused")))
            {
                Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
                Plugin.Instance.CaptureException(ex);
            }
            return false;
        }
    }

    private bool ScanDirectory(string partialPath, string fullPath, Ordering.GroupFilterType filterType, bool shouldIgnore)
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
        if (filterType != Ordering.GroupFilterType.Default) {
            var isShowLibrary = filterType == Ordering.GroupFilterType.Others;
            var isMovieSeason = season.AniDB.Type == SeriesType.Movie;
            if (isMovieSeason == isShowLibrary) {
                Logger.LogInformation("Library separation is enabled, ignoring shoko series. (Series={SeriesId})", season.Id);
                return true;
            }
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

        // Enable the scanner if we selected to use the Shoko provider for any metadata type on the current root folder.
        if (!Lookup.IsEnabledForItem(parent, out var isSoleProvider))
            return null;

        var fullPath = fileInfo.FullName;
        var (mediaFolder, partialPath) = ApiManager.FindMediaFolder(fullPath, parent, root);
        if (mediaFolder == root)
            return null;

        if (parent.Id == mediaFolder.Id && fileInfo.IsDirectory) {
            var isVFS = GenerateFullStructureForMediaFolder(mediaFolder);
            if (!isVFS)
                return null;

            if (!int.TryParse(fileInfo.Name.Split('-').LastOrDefault(), out var seriesId))
                return null;

            return new TvSeries()
            {
                Path = fileInfo.FullName,
            };
        }

        return null;
    }

    public MultiItemResolverResult? ResolveMultiple(Folder? parent, string? collectionType, List<FileSystemMetadata> fileInfoList)
    {
        // Disable resolver.
        if (!Plugin.Instance.Configuration.EXPERIMENTAL_EnableResolver)
            return new();

        var root = LibraryManager.RootFolder;
        if (!(collectionType == CollectionType.TvShows || collectionType == CollectionType.Movies || collectionType == null) || root == null || parent == null || parent == root)
            return new();

        // Redirect children of a VFS managed folder to the VFS series.
        if (parent.GetParent() == root) {
            var isVFS = GenerateFullStructureForMediaFolder(parent);
            if (!isVFS)
                return new();

            var filterType = !Plugin.Instance.Configuration.FilterOnLibraryTypes ? (
                Ordering.GroupFilterType.Default
            ) : (
                collectionType switch {
                    "movies" => Ordering.GroupFilterType.Movies,
                    _ => Ordering.GroupFilterType.Others,
                }
            );
            var items = FileSystem.GetDirectories(ShokoAPIManager.GetVirtualRootForMediaFolder(parent))
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

    #endregion
}