using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Models;
using Shokofin.Utils;

using Path = System.IO.Path;

namespace Shokofin
{
    public class LibraryScanner : IResolverIgnoreRule
    {
        private readonly ShokoAPIManager ApiManager;

        private readonly IIdLookup Lookup;

        private readonly ILibraryManager LibraryManager;

        private readonly IFileSystem FileSystem;

        private readonly ILogger<LibraryScanner> Logger;

        public LibraryScanner(ShokoAPIManager apiManager, IIdLookup lookup, ILibraryManager libraryManager, IFileSystem fileSystem, ILogger<LibraryScanner> logger)
        {
            ApiManager = apiManager;
            Lookup = lookup;
            LibraryManager = libraryManager;
            FileSystem = fileSystem;
            Logger = logger;
        }

        /// <summary>
        /// It's not really meant to be used this way, but this is our library
        /// "scanner". It scans the files and folders, and conditionally filters
        /// out _some_ of the files and/or folders.
        /// </summary>
        /// <param name="fileInfo"></param>
        /// <param name="parent"></param>
        /// <returns>True if the entry should be ignored.</returns>
        public bool ShouldIgnore(FileSystemMetadata fileInfo, BaseItem parent)
        {
            // Everything in the root folder is ignored by us.
            var root = LibraryManager.RootFolder;
            if (fileInfo == null || parent == null || root == null || parent == root || !(parent is Folder parentFolder) || fileInfo.FullName.StartsWith(root.Path))
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
                var mediaFolder = ApiManager.FindMediaFolder(fullPath, parentFolder, root);
                var partialPath = fullPath.Substring(mediaFolder.Path.Length);
                var shouldIgnore = Plugin.Instance.Configuration.LibraryFilteringMode ?? isSoleProvider;
                if (fileInfo.IsDirectory)
                    return ScanDirectory(partialPath, fullPath, LibraryManager.GetInheritedContentType(parentFolder), shouldIgnore);
                else
                    return ScanFile(partialPath, fullPath, shouldIgnore);
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

        private bool ScanDirectory(string partialPath, string fullPath, string libraryType, bool shouldIgnore)
        {
            var season = ApiManager.GetSeasonInfoByPath(fullPath)
                .GetAwaiter()
                .GetResult();

            // We inform/warn here since we enabled the provider in our library, but we can't find a match for the given folder path.
            if (season == null) {
                // If we're in strict mode, then check the sub-directories if we have a <Show>/<Season>/<Episodes> structure.
                if (shouldIgnore && partialPath[1..].Split(Path.DirectorySeparatorChar).Length == 1) {
                    var entries = FileSystem.GetDirectories(fullPath, false).ToList();
                    Logger.LogDebug("Unable to find shoko series for {Path}, trying {DirCount} sub-directories.", entries.Count, partialPath);
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
                if (season == null) {
                    if (shouldIgnore)
                        Logger.LogInformation("Ignored unknown folder at path {Path}", partialPath);
                    else
                        Logger.LogWarning("Skipped unknown folder at path {Path}", partialPath);
                    return shouldIgnore;
                }
            }

            API.Info.ShowInfo show = null;
            // Filter library if we enabled the option.
            if (Plugin.Instance.Configuration.FilterOnLibraryTypes) switch (libraryType) {
                case "tvshows":
                    if (season.AniDB.Type == SeriesType.Movie) {
                        Logger.LogInformation("Library separation is enabled, ignoring series. (Series={SeriesId})", season.Id);
                        return true;
                    }

                    // If we're using series grouping, pre-load the group now to help reduce load times later.
                    show = ApiManager.GetShowInfoForSeries(season.Id, Ordering.GroupFilterType.Others)
                        .GetAwaiter()
                        .GetResult();
                    break;
                case "movies":
                    if (season.AniDB.Type != SeriesType.Movie) {
                        Logger.LogInformation("Library separation is enabled, ignoring series. (Series={SeriesId})", season.Id);
                        return true;
                    }

                    // If we're using series grouping, pre-load the group now to help reduce load times later.
                    show = ApiManager.GetShowInfoForSeries(season.Id, Ordering.GroupFilterType.Movies)
                        .GetAwaiter()
                        .GetResult();
                    break;
            }
            // If we're using series grouping, pre-load the group now to help reduce load times later.
            else
                show = ApiManager.GetShowInfoForSeries(season.Id)
                    .GetAwaiter()
                    .GetResult();

            if (show != null)
                Logger.LogInformation("Found shoko group {GroupName} (Series={SeriesId},Group={GroupId})", show.Name, season.Id, show.Id);
            else
                Logger.LogInformation("Found shoko series {SeriesName} (Series={SeriesId})", season.Shoko.Name, season.Id);

            return false;
        }

        private bool ScanFile(string partialPath, string fullPath, bool shouldIgnore)
        {
            var (file, series, _) = ApiManager.GetFileInfoByPath(fullPath, null)
                .GetAwaiter()
                .GetResult();

            // We inform/warn here since we enabled the provider in our library, but we can't find a match for the given file path.
            if (file == null) {
                if (shouldIgnore)
                    Logger.LogInformation("Ignored unknown file at path {Path}", partialPath);
                else
                    Logger.LogWarning("Skipped unknown file at path {Path}", partialPath); 
                return shouldIgnore;
            }

            Logger.LogInformation("Found {EpisodeCount} shoko episode(s) for {SeriesName} (Series={SeriesId},File={FileId})", file.EpisodeList.Count, series.Shoko.Name, series.Id, file.Id);

            // We're going to post process this file later, but we don't want to include it in our library for now.
            if (file.ExtraType != null) {
                Logger.LogInformation("File was assigned an extra type, ignoring file. (Series={SeriesId},File={FileId})", series.Id, file.Id);
                return true;
            }

            return false;
        }
    }
}