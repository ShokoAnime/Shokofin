using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Emby.Naming.Common;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Models;
using Shokofin.ExternalIds;

using File = System.IO.File;
using IDirectoryService = MediaBrowser.Controller.Providers.IDirectoryService;
using TvSeries = MediaBrowser.Controller.Entities.TV.Series;

namespace Shokofin.Resolvers;
#pragma warning disable CS8768

public class ShokoResolver : IItemResolver, IMultiItemResolver
{
    private readonly ILogger<ShokoResolver> Logger;

    private readonly IIdLookup Lookup;

    private readonly ILibraryManager LibraryManager;

    private readonly IFileSystem FileSystem;

    private readonly ShokoAPIManager ApiManager;

    private readonly VirtualFileSystemService ResolveManager;

    private readonly NamingOptions NamingOptions;

    public ShokoResolver(
        ILogger<ShokoResolver> logger,
        IIdLookup lookup,
        ILibraryManager libraryManager,
        IFileSystem fileSystem,
        ShokoAPIManager apiManager,
        VirtualFileSystemService resolveManager,
        NamingOptions namingOptions
    )
    {
        Logger = logger;
        Lookup = lookup;
        LibraryManager = libraryManager;
        FileSystem = fileSystem;
        ApiManager = apiManager;
        ResolveManager = resolveManager;
        NamingOptions = namingOptions;
    }

    public async Task<BaseItem?> ResolveSingle(Folder? parent, CollectionType? collectionType, FileSystemMetadata fileInfo)
    {
        if (!(collectionType is CollectionType.tvshows or CollectionType.movies or null) || parent is null || fileInfo is null)
            return null;

        var root = LibraryManager.RootFolder;
        if (root is null || parent == root)
            return null;

        Guid? trackerId = null;
        try {
            if (!Lookup.IsEnabledForItem(parent))
                return null;

            // Skip anything outside the VFS.
            if (!fileInfo.FullName.StartsWith(Plugin.Instance.VirtualRoot))
                return null;

            if (parent.GetTopParent() is not Folder mediaFolder)
                return null;

            trackerId = Plugin.Instance.Tracker.Add($"Resolve path \"{fileInfo.FullName}\".");
            var (vfsPath, shouldContinue) = await ResolveManager.GenerateStructureInVFS(mediaFolder, collectionType, fileInfo.FullName).ConfigureAwait(false);
            if (string.IsNullOrEmpty(vfsPath) || !shouldContinue)
                return null;

            if (parent.Id == mediaFolder.Id && fileInfo.IsDirectory) {
                if (!fileInfo.Name.TryGetAttributeValue(ShokoSeriesId.Name, out var seriesId) || !int.TryParse(seriesId, out _))
                    return null;

                return new TvSeries() {
                    Path = fileInfo.FullName,
                };
            }

            return null;
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
            throw;
        }
        finally {
            if (trackerId.HasValue)
                Plugin.Instance.Tracker.Remove(trackerId.Value);
        }
    }

    public async Task<MultiItemResolverResult?> ResolveMultiple(Folder? parent, CollectionType? collectionType, List<FileSystemMetadata> fileInfoList)
    {
        if (!(collectionType is CollectionType.tvshows or CollectionType.movies or null) || parent is null)
            return null;

        var root = LibraryManager.RootFolder;
        if (root is null || parent == root)
            return null;

        Guid? trackerId = null;
        try {
            if (!Lookup.IsEnabledForItem(parent))
                return null;

            if (parent.GetTopParent() is not Folder mediaFolder)
                return null;

            trackerId = Plugin.Instance.Tracker.Add($"Resolve children of \"{parent.Path}\". (Children={fileInfoList.Count})");
            var (vfsPath, shouldContinue) = await ResolveManager.GenerateStructureInVFS(mediaFolder, collectionType, parent.Path).ConfigureAwait(false);
            if (string.IsNullOrEmpty(vfsPath) || !shouldContinue)
                return null;

            // Redirect children of a VFS managed media folder to the VFS.
            if (parent.IsTopParent) {
                var createMovies = collectionType is CollectionType.movies || (collectionType is null && Plugin.Instance.Configuration.SeparateMovies);
                var pathsToRemoveBag = new ConcurrentBag<(string, bool)>();
                var items = FileSystem.GetDirectories(vfsPath)
                    .AsParallel()
                    .SelectMany(dirInfo => {
                        if (!dirInfo.Name.TryGetAttributeValue(ShokoSeriesId.Name, out var seriesId) || !int.TryParse(seriesId, out _))
                            return [];

                        var season = ApiManager.GetSeasonInfoForSeries(seriesId)
                            .ConfigureAwait(false)
                            .GetAwaiter()
                            .GetResult();
                        if (season is null) {
                            pathsToRemoveBag.Add((dirInfo.FullName, true));
                            return [];
                        }

                        if (createMovies && (season.Type is SeriesType.Movie || collectionType is CollectionType.movies && !Plugin.Instance.Configuration.FilterMovieLibraries)) {
                            return FileSystem.GetFiles(dirInfo.FullName)
                                .AsParallel()
                                .Select(fileInfo => {
                                    // Only allow the video files, since the subtitle files also have the ids set.
                                    if (!NamingOptions.VideoFileExtensions.Contains(Path.GetExtension(fileInfo.Name)))
                                        return null;

                                    if (!VirtualFileSystemService.TryGetIdsForPath(fileInfo.FullName, out seriesId, out var fileId))
                                        return null;

                                    // This will hopefully just re-use the pre-cached entries from the cache, but it may
                                    // also get it from remote if the cache was emptied for whatever reason.
                                    var file = ApiManager.GetFileInfo(fileId, seriesId)
                                        .ConfigureAwait(false)
                                        .GetAwaiter()
                                        .GetResult();

                                    // Abort if the file was not recognized.
                                    if (file is null) {
                                        pathsToRemoveBag.Add((fileInfo.FullName, false));
                                        return null;
                                    }

                                    // Or if it's a recognized extra.
                                    if (file.EpisodeList.Any(eI => season.IsExtraEpisode(eI.Episode)))
                                        return null;

                                    return new Movie() {
                                        Path = fileInfo.FullName,
                                    } as BaseItem;
                                })
                                .ToArray();
                        }

                        return [
                            new TvSeries() {
                                Path = dirInfo.FullName,
                            },
                        ];
                    })
                    .OfType<BaseItem>()
                    .ToList();

                if (!pathsToRemoveBag.IsEmpty) {
                    var start = DateTime.Now;
                    var pathsToRemove = pathsToRemoveBag.ToArray().DistinctBy(tuple => tuple.Item1).ToList();
                    Logger.LogDebug("Cleaning up {Count} removed entries in {Path}", pathsToRemove.Count, mediaFolder.Path);
                    foreach (var (pathToRemove, isDirectory) in pathsToRemove) {
                        try {
                            if (isDirectory) {
                                Logger.LogTrace("Removing directory: {Path}", pathToRemove);
                                Directory.Delete(pathToRemove, true);
                                Logger.LogTrace("Removed directory: {Path}", pathToRemove);
                                
                            }
                            else {
                                Logger.LogTrace("Removing file: {Path}", pathToRemove);
                                File.Delete(pathToRemove);
                                Logger.LogTrace("Removed file: {Path}", pathToRemove);
                            }
                        }
                        catch (Exception ex) {
                            Logger.LogTrace(ex, "Failed to remove {Path}", pathToRemove);
                        }
                    }

                    var deltaTime = DateTime.Now - start;
                    Logger.LogDebug("Cleaned up {Count} removed entries in {Time}", pathsToRemove.Count, deltaTime);
                }

                var keepFile = Path.Join(vfsPath, ".keep");
                if (File.Exists(keepFile)) {
                    Logger.LogTrace("Removing now unneeded keep file: {Path}", keepFile);
                    File.Delete(keepFile);
                }

                // TODO: uncomment the code snippet once we reach JF 10.10.
                // return new() { Items = items, ExtraFiles = new() };

                // TODO: Remove these two hacks once we have proper support for adding multiple series at once.
                if (!items.Any(i => i is Movie) && items.Count > 0) {
                    fileInfoList.Clear();
                    fileInfoList.AddRange(items.OrderBy(s => int.Parse(s.Path.GetAttributeValue(ShokoSeriesId.Name)!)).Select(s => FileSystem.GetFileSystemInfo(s.Path)));
                }

                return new() { Items = items.Where(i => i is Movie).ToList(), ExtraFiles = items.OfType<TvSeries>().Select(s => FileSystem.GetFileSystemInfo(s.Path)).ToList() };
            }

            return null;
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
            throw;
        }
        finally {
            if (trackerId.HasValue)
                Plugin.Instance.Tracker.Remove(trackerId.Value);
        }
    }

    #region IItemResolver

    ResolverPriority IItemResolver.Priority => ResolverPriority.Plugin;

    BaseItem? IItemResolver.ResolvePath(ItemResolveArgs args)
        => ResolveSingle(args.Parent, args.CollectionType, args.FileInfo)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

    #endregion
    
    #region IMultiItemResolver

    MultiItemResolverResult? IMultiItemResolver.ResolveMultiple(Folder parent, List<FileSystemMetadata> files, CollectionType? collectionType, IDirectoryService directoryService)
        => ResolveMultiple(parent, collectionType, files)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

    #endregion
}
