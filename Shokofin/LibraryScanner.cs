using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Resolvers;
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

        private readonly ILogger<LibraryScanner> Logger;

        public LibraryScanner(ShokoAPIManager apiManager, IIdLookup lookup, ILibraryManager libraryManager, ILogger<LibraryScanner> logger)
        {
            ApiManager = apiManager;
            Lookup = lookup;
            LibraryManager = libraryManager;
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
        public bool ShouldIgnore(MediaBrowser.Model.IO.FileSystemMetadata fileInfo, BaseItem parent)
        {
            // Everything in the root folder is ignored by us.
            var root = LibraryManager.RootFolder;
            if (fileInfo == null || parent == null || root == null || parent == root || !(parent is Folder) || fileInfo.FullName.StartsWith(root.Path))
                return false;

            try {
                // Enable the scanner if we selected to use the Shoko provider for any metadata type on the current root folder.
                if (!Lookup.IsEnabledForItem(parent))
                    return false;

                if (fileInfo.IsDirectory &&  Plugin.Instance.IgnoredFolders.Contains(Path.GetFileName(fileInfo.FullName).ToLowerInvariant())) {
                    Logger.LogDebug("Skipped excluded folder at path {Path}", fileInfo.FullName);
                    return true;
                }

                if (!fileInfo.IsDirectory && Plugin.Instance.IgnoredFileExtensions.Contains(fileInfo.Extension.ToLowerInvariant())) {
                    Logger.LogDebug("Skipped excluded file at path {Path}", fileInfo.FullName);
                    return false;
                }

                var fullPath = fileInfo.FullName;
                var mediaFolder = ApiManager.FindMediaFolder(fullPath, parent as Folder, root);
                var partialPath = fullPath.Substring(mediaFolder.Path.Length);
                if (fileInfo.IsDirectory)
                    return ScanDirectory(partialPath, fullPath, LibraryManager.GetInheritedContentType(parent));
                else
                    return ScanFile(partialPath, fullPath);
            }
            catch (System.Exception e) {
                if (!(e is System.Net.Http.HttpRequestException && e.Message.Contains("Connection refused")))
                    Logger.LogError(e, $"Threw unexpectedly - {e.Message}");
                return false;
            }
        }

        private bool ScanDirectory(string partialPath, string fullPath, string libraryType)
        {
            var includeGroup = Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup;
            var series = ApiManager.GetSeriesInfoByPath(fullPath)
                .GetAwaiter()
                .GetResult();

            // We warn here since we enabled the provider in our library, but we can't find a match for the given folder path.
            if (series == null) {
                Logger.LogWarning("Skipped unknown folder at path {Path}", partialPath);
                return false;
            }

            API.Info.GroupInfo group = null;
            // Filter library if we enabled the option.
            if (Plugin.Instance.Configuration.FilterOnLibraryTypes) switch (libraryType) {
                case "tvshows":
                    if (series.AniDB.Type == SeriesType.Movie) {
                        Logger.LogInformation("Library separation is enabled, ignoring series. (Series={SeriesId})", series.Id);
                        return true;
                    }

                    // If we're using series grouping, pre-load the group now to help reduce load times later.
                    if (includeGroup)
                        group = ApiManager.GetGroupInfoForSeries(series.Id, Ordering.GroupFilterType.Others)
                            .GetAwaiter()
                            .GetResult();
                    break;
                case "movies":
                    if (series.AniDB.Type != SeriesType.Movie) {
                        Logger.LogInformation("Library separation is enabled, ignoring series. (Series={SeriesId})", series.Id);
                        return true;
                    }

                    // If we're using series grouping, pre-load the group now to help reduce load times later.
                    if (includeGroup)
                        group = ApiManager.GetGroupInfoForSeries(series.Id, Ordering.GroupFilterType.Movies)
                            .GetAwaiter()
                            .GetResult();
                    break;
            }
            // If we're using series grouping, pre-load the group now to help reduce load times later.
            else if (includeGroup)
                group = ApiManager.GetGroupInfoForSeries(series.Id)
                    .GetAwaiter()
                    .GetResult();

            if (group != null)
                Logger.LogInformation("Found group {GroupName} (Series={SeriesId},Group={GroupId})", group.Shoko.Name, series.Id, group.Id);
            else
                Logger.LogInformation("Found series {SeriesName} (Series={SeriesId})", series.Shoko.Name, series.Id);

            return false;
        }

        private bool ScanFile(string partialPath, string fullPath)
        {
            var includeGroup = Plugin.Instance.Configuration.SeriesGrouping == Ordering.GroupType.ShokoGroup;
            var config = Plugin.Instance.Configuration;
            var (file, series, _group) = ApiManager.GetFileInfoByPath(fullPath, null)
                .GetAwaiter()
                .GetResult();

            // We warn here since we enabled the provider in our library, but we can't find a match for the given file path.
            if (file == null) {   
                Logger.LogWarning("Skipped unknown file at path {Path}", partialPath);
                return false;
            }

            Logger.LogInformation("Found {EpisodeCount} episode(s) for {SeriesName} (Series={SeriesId},File={FileId})", file.EpisodeList.Count, series.Shoko.Name, series.Id, file.Id);

            // We're going to post process this file later, but we don't want to include it in our library for now.
            if (file.ExtraType != null) {
                Logger.LogInformation("File was assigned an extra type, ignoring file. (Series={SeriesId},File={FileId})", series.Id, file.Id);
                return true;
            }

            return false;
        }
    }
}