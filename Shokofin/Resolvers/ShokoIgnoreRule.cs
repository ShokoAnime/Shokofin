using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Emby.Naming.Common;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Models;
using Shokofin.Configuration;
using Shokofin.Utils;

namespace Shokofin.Resolvers;
#pragma warning disable CS8766

public class ShokoIgnoreRule : IResolverIgnoreRule
{
    private readonly ILogger<ShokoIgnoreRule> Logger;

    private readonly IIdLookup Lookup;

    private readonly ILibraryManager LibraryManager;

    private readonly IFileSystem FileSystem;

    private readonly ShokoAPIManager ApiManager;

    private readonly MediaFolderConfigurationService ConfigurationService;

    private readonly NamingOptions NamingOptions;

    public ShokoIgnoreRule(
        ILogger<ShokoIgnoreRule> logger,
        IIdLookup lookup,
        ILibraryManager libraryManager,
        IFileSystem fileSystem,
        ShokoAPIManager apiManager,
        MediaFolderConfigurationService configurationService,
        NamingOptions namingOptions
    )
    {
        Lookup = lookup;
        Logger = logger;
        LibraryManager = libraryManager;
        FileSystem = fileSystem;
        ApiManager = apiManager;
        ConfigurationService = configurationService;
        NamingOptions = namingOptions;
    }

    public async Task<bool> ShouldFilterItem(Folder? parent, FileSystemMetadata fileInfo)
    {
        // Check if the parent is not made yet, or the file info is missing.
        if (parent is null || fileInfo is null)
            return false;

        // Check if the root is not made yet. This should **never** be false at
        // this point in time, but if it is, then bail.
        var root = LibraryManager.RootFolder;
        if (root is null || parent.Id == root.Id)
            return false;

        // Assume anything within the VFS is already okay.
        if (fileInfo.FullName.StartsWith(Plugin.Instance.VirtualRoot))
            return false;

        Guid? trackerId = null;
        try {
            // Enable the scanner if we selected to use the Shoko provider for any metadata type on the current root folder.
            if (!Lookup.IsEnabledForItem(parent, out var isSoleProvider))
                return false;

            trackerId = Plugin.Instance.Tracker.Add($"Should ignore path \"{fileInfo.FullName}\".");
            if (fileInfo.IsDirectory && Plugin.Instance.IgnoredFolders.Contains(Path.GetFileName(fileInfo.FullName).ToLowerInvariant())) {
                Logger.LogDebug("Skipped excluded folder at path {Path}", fileInfo.FullName);
                return true;
            }

            if (!fileInfo.IsDirectory && !NamingOptions.VideoFileExtensions.Contains(fileInfo.Extension.ToLowerInvariant())) {
                Logger.LogDebug("Skipped excluded file at path {Path}", fileInfo.FullName);
                return false;
            }

            var fullPath = fileInfo.FullName;
            var (mediaFolder, partialPath) = ApiManager.FindMediaFolder(fullPath, parent);

            // Ignore any media folders that aren't mapped to shoko.
            var mediaFolderConfig = ConfigurationService.GetOrCreateConfigurationForMediaFolder(mediaFolder);
            if (!mediaFolderConfig.IsMapped) {
                Logger.LogDebug("Skipped media folder for path {Path} (MediaFolder={MediaFolderId})", fileInfo.FullName, mediaFolderConfig.MediaFolderId);
                return false;
            }

            // Filter out anything in the media folder if the VFS is enabled,
            // because the VFS is pre-filtered, and we should **never** reach
            // this point except for the folders in the root of the media folder
            // that we're not even going to use.
            if (mediaFolderConfig.IsVirtualFileSystemEnabled || mediaFolderConfig.IsVirtualRoot)
                return true;

            var shouldIgnore = mediaFolderConfig.LibraryFilteringMode switch {
                Ordering.LibraryFilteringMode.Strict => true,
                Ordering.LibraryFilteringMode.Lax => false,
                // Ordering.LibraryFilteringMode.Auto =>
                _ => mediaFolderConfig.IsVirtualFileSystemEnabled  || isSoleProvider,
            };
            var collectionType = LibraryManager.GetInheritedContentType(mediaFolder);
            if (fileInfo.IsDirectory)
                return await ShouldFilterDirectory(partialPath, fullPath, collectionType, shouldIgnore).ConfigureAwait(false);

            return await ShouldFilterFile(partialPath, fullPath, shouldIgnore).ConfigureAwait(false);
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

    private async Task<bool> ShouldFilterDirectory(string partialPath, string fullPath, CollectionType? collectionType, bool shouldIgnore)
    {
        var season = await ApiManager.GetSeasonInfoByPath(fullPath).ConfigureAwait(false);

        // We inform/warn here since we enabled the provider in our library, but we can't find a match for the given folder path.
        if (season == null) {
            // If we're in strict mode, then check the sub-directories if we have a <Show>/<Season>/<Episodes> structure.
            if (shouldIgnore && partialPath[1..].Split(Path.DirectorySeparatorChar).Length is 1) {
                try {
                    var entries = FileSystem.GetDirectories(fullPath, false).ToList();
                    Logger.LogDebug("Unable to find shoko series for {Path}, trying {DirCount} sub-directories.", partialPath, entries.Count);
                    foreach (var entry in entries) {
                        season = await ApiManager.GetSeasonInfoByPath(entry.FullName).ConfigureAwait(false);
                        if (season is not null) {
                            Logger.LogDebug("Found shoko series {SeriesName} for sub-directory of path {Path} (Series={SeriesId},ExtraSeries={ExtraIds})", season.Shoko.Name, partialPath, season.Id, season.ExtraIds);
                            break;
                        }
                    }
                }
                catch (DirectoryNotFoundException) { }
            }
            if (season is null) {
                if (shouldIgnore)
                    Logger.LogInformation("Ignored unknown folder at path {Path}", partialPath);
                else
                    Logger.LogWarning("Skipped unknown folder at path {Path}", partialPath);
                return shouldIgnore;
            }
        }

        // Filter library if we enabled the option.
        var isMovieSeason = season.Type is SeriesType.Movie;
        switch (collectionType) {
            case CollectionType.tvshows:
                if (isMovieSeason && Plugin.Instance.Configuration.SeparateMovies) {
                    Logger.LogInformation("Found movie in show library and library separation is enabled, ignoring shoko series. (Series={SeriesId},ExtraSeries={ExtraIds})", season.Id, season.ExtraIds);
                    return true;
                }
                break;
            case CollectionType.movies:
                if (!isMovieSeason && Plugin.Instance.Configuration.FilterMovieLibraries) {
                    Logger.LogInformation("Found show in movie library, ignoring shoko series. (Series={SeriesId},ExtraSeries={ExtraIds})", season.Id, season.ExtraIds);
                    return true;
                }
                break;
        }

        var show = await ApiManager.GetShowInfoForSeries(season.Id).ConfigureAwait(false)!;
        if (!string.IsNullOrEmpty(show!.GroupId))
            Logger.LogInformation("Found shoko group {GroupName} (Series={SeriesId},ExtraSeries={ExtraIds},Group={GroupId})", show.Name, season.Id, season.ExtraIds, show.GroupId);
        else
            Logger.LogInformation("Found shoko series {SeriesName} (Series={SeriesId},ExtraSeries={ExtraIds})", season.Shoko.Name, season.Id, season.ExtraIds);

        return false;
    }

    private async Task<bool> ShouldFilterFile(string partialPath, string fullPath, bool shouldIgnore)
    {
        var (file, season, _) = await ApiManager.GetFileInfoByPath(fullPath).ConfigureAwait(false);

        // We inform/warn here since we enabled the provider in our library, but we can't find a match for the given file path.
        if (file is null || season is null) {
            if (shouldIgnore)
                Logger.LogInformation("Ignored unknown file at path {Path}", partialPath);
            else
                Logger.LogWarning("Skipped unknown file at path {Path}", partialPath);
            return shouldIgnore;
        }

        Logger.LogInformation("Found {EpisodeCount} shoko episode(s) for {SeriesName} (Series={SeriesId},ExtraSeries={ExtraIds},File={FileId})", file.EpisodeList.Count, season.Shoko.Name, season.Id, season.ExtraIds, file.Id);

        // We're going to post process this file later, but we don't want to include it in our library for now.
        if (file.EpisodeList.Any(eI => season.IsExtraEpisode(eI.Episode))) {
            Logger.LogInformation("File was assigned an extra type, ignoring file. (Series={SeriesId},ExtraSeries={ExtraIds},File={FileId})", season.Id, season.ExtraIds, file.Id);
            return true;
        }

        return false;
    }

    #region IResolverIgnoreRule Implementation

    bool IResolverIgnoreRule.ShouldIgnore(FileSystemMetadata fileInfo, BaseItem? parent)
        => ShouldFilterItem(parent as Folder, fileInfo)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

    #endregion
}
