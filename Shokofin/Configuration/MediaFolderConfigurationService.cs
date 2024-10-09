using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Naming.Common;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Configuration.Models;
using Shokofin.Utils;

namespace Shokofin.Configuration;

public static class MediaFolderConfigurationExtensions
{
    public static Folder GetFolderForPath(this string mediaFolderPath)
        => BaseItem.LibraryManager.FindByPath(mediaFolderPath, true) as Folder ??
            throw new Exception($"Unable to find folder by path \"{mediaFolderPath}\".");

    public static IReadOnlyList<(int importFolderId, string importFolderSubPath, IReadOnlyList<string> mediaFolderPaths)> ToImportFolderList(this IEnumerable<MediaFolderConfiguration> mediaConfigs)
        => mediaConfigs
            .GroupBy(a => (a.ImportFolderId, a.ImportFolderRelativePath))
            .Select(g => (g.Key.ImportFolderId, g.Key.ImportFolderRelativePath, g.Select(a => a.MediaFolderPath).ToList() as IReadOnlyList<string>))
            .ToList();

    public static IReadOnlyList<(string importFolderSubPath, bool vfsEnabled, IReadOnlyList<string> mediaFolderPaths)> ToImportFolderList(this IEnumerable<MediaFolderConfiguration> mediaConfigs, int importFolderId, string relativePath)
        => mediaConfigs
            .Where(a => a.ImportFolderId == importFolderId && a.IsEnabledForPath(relativePath))
            .GroupBy(a => (a.ImportFolderId, a.ImportFolderRelativePath, a.IsVirtualFileSystemEnabled))
            .Select(g => (g.Key.ImportFolderRelativePath, g.Key.IsVirtualFileSystemEnabled, g.Select(a => a.MediaFolderPath).ToList() as IReadOnlyList<string>))
            .ToList();
}

public class MediaFolderConfigurationService
{
    private readonly ILogger<MediaFolderConfigurationService> Logger;

    private readonly ILibraryManager LibraryManager;

    private readonly IFileSystem FileSystem;

    private readonly IDirectoryService DirectoryService;

    private readonly LibraryScanWatcher LibraryScanWatcher;

    private readonly IIdLookup Lookup;

    private readonly UsageTracker UsageTracker;

    private readonly ShokoAPIClient ApiClient;

    private readonly NamingOptions NamingOptions;

    private readonly Dictionary<Guid, string> MediaFolderChangeKeys = [];

    private readonly Dictionary<Guid, (string libraryName, HashSet<string> add, HashSet<string> remove)> LibraryEdits = [];

    private bool ShouldGenerateAllConfigurations = true;

    private readonly SemaphoreSlim LockObj = new(1, 1);

    public event EventHandler<MediaConfigurationChangedEventArgs>? ConfigurationAdded;

    public event EventHandler<MediaConfigurationChangedEventArgs>? ConfigurationUpdated;

    public event EventHandler<MediaConfigurationChangedEventArgs>? ConfigurationRemoved;

    public MediaFolderConfigurationService(
        ILogger<MediaFolderConfigurationService> logger,
        ILibraryManager libraryManager,
        IFileSystem fileSystem,
        IDirectoryService directoryService,
        LibraryScanWatcher libraryScanWatcher,
        IIdLookup lookup,
        UsageTracker usageTracker,
        ShokoAPIClient apiClient,
        NamingOptions namingOptions
    )
    {
        Logger = logger;
        LibraryManager = libraryManager;
        FileSystem = fileSystem;
        DirectoryService = directoryService;
        LibraryScanWatcher = libraryScanWatcher;
        Lookup = lookup;
        UsageTracker = usageTracker;
        ApiClient = apiClient;
        NamingOptions = namingOptions;

        foreach (var mediaConfig in Plugin.Instance.Configuration.MediaFolders)
            MediaFolderChangeKeys[mediaConfig.MediaFolderId] = ConstructKey(mediaConfig);
        UsageTracker.Stalled += OnUsageTrackerStalled;
        LibraryScanWatcher.ValueChanged += OnLibraryScanValueChanged;
        LibraryManager.ItemRemoved += OnLibraryManagerItemRemoved;
        Plugin.Instance.ConfigurationChanged += OnConfigurationChanged;
    }

    ~MediaFolderConfigurationService()
    {
        LibraryManager.ItemRemoved -= OnLibraryManagerItemRemoved;
        Plugin.Instance.ConfigurationChanged -= OnConfigurationChanged;
        LibraryScanWatcher.ValueChanged -= OnLibraryScanValueChanged;
        UsageTracker.Stalled -= OnUsageTrackerStalled;
        MediaFolderChangeKeys.Clear();
        LockObj.Dispose();
    }

    #region Changes Tracking

    private void OnLibraryScanValueChanged(object? sender, bool isRunning)
    {
        if (isRunning)
            return;

        Task.Run(() => EditLibraries(true));
    }

    private void OnUsageTrackerStalled(object? sender, EventArgs eventArgs)
    {
        Task.Run(() => EditLibraries(false));
    }

    private async Task EditLibraries(bool shouldScheduleLibraryScan)
    {
        await LockObj.WaitAsync().ConfigureAwait(false);
        try {
            ShouldGenerateAllConfigurations = true;

            if (LibraryEdits.Count is 0)
                return;

            var libraryEdits = LibraryEdits.ToList();
            LibraryEdits.Clear();
            foreach (var (libraryId, (libraryName, add, remove)) in libraryEdits) {
                foreach (var vfsPath in add)
                {
                    // Before we add the media folder we need to
                    //   a) make sure it exists so we can add it without Jellyfin throwing a fit, and
                    //   b) make sure it's not empty to make sure Jellyfin doesn't skip resolving it.
                    if (!Directory.Exists(vfsPath))
                        Directory.CreateDirectory(vfsPath);
                    if (!FileSystem.GetFileSystemEntryPaths(vfsPath).Any())
                        File.WriteAllText(Path.Join(vfsPath, ".keep"), string.Empty);

                    LibraryManager.AddMediaPath(libraryName, new(vfsPath));
                }
                foreach (var vfsPath in remove)
                    LibraryManager.RemoveMediaPath(libraryName, new(vfsPath));
            }
            if (shouldScheduleLibraryScan)
                await LibraryManager.ValidateMediaLibrary(new Progress<double>(), CancellationToken.None);
        }
        finally {
            LockObj.Release();
        }
    }

    private static string ConstructKey(MediaFolderConfiguration config)
        => $"IsMapped={config.IsMapped},IsFileEventsEnabled={config.IsFileEventsEnabled},IsRefreshEventsEnabled={config.IsRefreshEventsEnabled},IsVirtualFileSystemEnabled={config.IsVirtualFileSystemEnabled},LibraryFilteringMode={config.LibraryFilteringMode}";

    private void OnConfigurationChanged(object? sender, PluginConfiguration config)
    {
        foreach (var mediaConfig in config.MediaFolders) {
            var currentKey = ConstructKey(mediaConfig);
            if (MediaFolderChangeKeys.TryGetValue(mediaConfig.MediaFolderId, out var previousKey) && previousKey != currentKey) {
                MediaFolderChangeKeys[mediaConfig.MediaFolderId] = currentKey;
                if (LibraryManager.GetItemById(mediaConfig.MediaFolderId) is not Folder mediaFolder)
                    continue;
                ConfigurationUpdated?.Invoke(sender, new(mediaConfig, mediaFolder));
            }
        }
    }

    private async void OnLibraryManagerItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        var root = LibraryManager.RootFolder;
        if (e.Item != null && root != null && e.Item != root && e.Item is Folder folder && folder.ParentId == Guid.Empty  && !string.IsNullOrEmpty(folder.Path) && !folder.Path.StartsWith(root.Path)) {
            await LockObj.WaitAsync();
            try {
                var mediaFolderConfig = Plugin.Instance.Configuration.MediaFolders.FirstOrDefault(c => c.MediaFolderId == folder.Id);
                if (mediaFolderConfig != null) {
                    Logger.LogDebug(
                        "Removing stored configuration for folder at {Path} (ImportFolder={ImportFolderId},RelativePath={RelativePath})",
                        folder.Path,
                        mediaFolderConfig.ImportFolderId,
                        mediaFolderConfig.ImportFolderRelativePath
                    );
                    Plugin.Instance.Configuration.MediaFolders.Remove(mediaFolderConfig);
                    Plugin.Instance.UpdateConfiguration();

                    MediaFolderChangeKeys.Remove(folder.Id);
                    ConfigurationRemoved?.Invoke(null, new(mediaFolderConfig, folder));
                }
            }
            finally {
                LockObj.Release();
            }
        }
    }

    #endregion

    #region Media Folder Mapping

    public IReadOnlyList<(string vfsPath, string mainMediaFolderPath, CollectionType? collectionType, IReadOnlyList<MediaFolderConfiguration> mediaList)> GetAvailableMediaFoldersForLibrariesForEvents(Func<MediaFolderConfiguration, bool>? filter = null)
    {
        LockObj.Wait();
        try {
            var virtualFolders = LibraryManager.GetVirtualFolders();
            var attachRoot = Plugin.Instance.Configuration.VFS_AttachRoot;
            return Plugin.Instance.Configuration.MediaFolders
                .Where(config => config.IsMapped && !config.IsVirtualRoot && (filter is null || filter(config)) && LibraryManager.GetItemById(config.MediaFolderId) is Folder)
                .GroupBy(config => config.LibraryId)
                .Select(groupBy => (
                    libraryFolder: LibraryManager.GetItemById(groupBy.Key) as Folder,
                    virtualFolder: virtualFolders.FirstOrDefault(folder => Guid.TryParse(folder.ItemId, out var guid) && guid == groupBy.Key),
                    mediaList: groupBy.ToList() as IReadOnlyList<MediaFolderConfiguration>
                ))
                .Where(tuple => tuple.libraryFolder is not null && tuple.virtualFolder is not null && tuple.virtualFolder.Locations.Length is > 0 && tuple.mediaList.Count is > 0)
                .Select(tuple => (
                    vfsPath: tuple.libraryFolder!.GetVirtualRoot(),
                    mainMediaFolderPath: attachRoot
                        ? tuple.libraryFolder!.GetVirtualRoot()
                        : tuple.virtualFolder!.Locations.FirstOrDefault(a => DirectoryService.IsAccessible(a)) ?? string.Empty,
                    collectionType: tuple.virtualFolder!.CollectionType.ConvertToCollectionType(),
                    tuple.mediaList
                ))
                .Where(tuple => !string.IsNullOrEmpty(tuple.vfsPath) && !string.IsNullOrEmpty(tuple.mainMediaFolderPath))
                .ToList();
        }
        finally {
            LockObj.Release();
        }
    }

    public async Task<(string vfsPath, string mainMediaFolderPath, IReadOnlyList<MediaFolderConfiguration> mediaList, bool skipGeneration)> GetMediaFoldersForLibraryInVFS(Folder mediaFolder, CollectionType? collectionType, Func<MediaFolderConfiguration, bool>? filter = null)
    {
        var mediaFolderConfig = await GetOrCreateConfigurationForMediaFolder(mediaFolder, collectionType);
        await LockObj.WaitAsync();
        try {
            var skipGeneration = LibraryEdits.Count is > 0 && LibraryManager.IsScanRunning;
            if (LibraryManager.GetItemById(mediaFolderConfig.LibraryId) is not Folder libraryFolder)
                return (string.Empty, string.Empty, [], skipGeneration);

            var virtualFolder = LibraryManager.GetVirtualFolders()
                .FirstOrDefault(folder => Guid.TryParse(folder.ItemId, out var guid) && guid == mediaFolderConfig.LibraryId);
            if (virtualFolder is null || virtualFolder.Locations.Length is 0)
                return (string.Empty, string.Empty, [], skipGeneration);

            var vfsPath = libraryFolder.GetVirtualRoot();
            var mediaFolders = Plugin.Instance.Configuration.MediaFolders
                .Where(config => config.IsMapped && !config.IsVirtualRoot && config.LibraryId == mediaFolderConfig.LibraryId && (filter is null || filter(config)) && LibraryManager.GetItemById(config.MediaFolderId) is Folder)
                .ToList();
            if (Plugin.Instance.Configuration.VFS_AttachRoot && mediaFolderConfig.IsVirtualFileSystemEnabled)
                return (vfsPath, vfsPath, mediaFolders, skipGeneration);

            var mainMediaFolderPath = virtualFolder.Locations.FirstOrDefault(a => DirectoryService.IsAccessible(a)) ?? string.Empty;
            return (vfsPath, mainMediaFolderPath, mediaFolders, skipGeneration);
        }
        finally {
            LockObj.Release();
        }
    }

    public async Task<MediaFolderConfiguration> GetOrCreateConfigurationForMediaFolder(Folder mediaFolder, CollectionType? collectionType = CollectionType.unknown)
    {
        await LockObj.WaitAsync();
        try {
            var allVirtualFolders = LibraryManager.GetVirtualFolders();
            if (allVirtualFolders.FirstOrDefault(p => p.Locations.Contains(mediaFolder.Path) && (collectionType is CollectionType.unknown || p.CollectionType.ConvertToCollectionType() == collectionType)) is not { } library || !Guid.TryParse(library.ItemId, out var libraryId))
                throw new Exception($"Unable to find any library to use for media folder \"{mediaFolder.Path}\"");

            if (ShouldGenerateAllConfigurations)
            {
                ShouldGenerateAllConfigurations = false;
                await GenerateAllConfigurations(allVirtualFolders).ConfigureAwait(false);
            }

            var config = Plugin.Instance.Configuration;
            var mediaFolderConfig = config.MediaFolders.First(c => c.MediaFolderId == mediaFolder.Id && c.LibraryId == libraryId);
            return mediaFolderConfig;
        }
        finally {
            LockObj.Release();
        }
    }

    private async Task GenerateAllConfigurations(List<VirtualFolderInfo> allVirtualFolders)
    {
        var filteredVirtualFolders = allVirtualFolders
            .Where(virtualFolder => 
                virtualFolder.CollectionType.ConvertToCollectionType() is null or CollectionType.movies or CollectionType.tvshows &&
                Lookup.IsEnabledForLibraryOptions(virtualFolder.LibraryOptions, out _)
            )
            .ToList();
        Logger.LogDebug("Found {Count} out of {TotalCount} libraries to check media folder configurations for.", filteredVirtualFolders.Count, allVirtualFolders.Count);
        var config = Plugin.Instance.Configuration;
        foreach (var virtualFolder in filteredVirtualFolders) {
            if (!Guid.TryParse(virtualFolder.ItemId, out var libraryId) || LibraryManager.GetItemById(libraryId) is not Folder libraryFolder)
                throw new Exception($"Unable to find virtual folder \"{virtualFolder.Name}\"");

            Logger.LogDebug("Checking {MediaFolderCount} media folders for library {LibraryName}. (Library={LibraryId})", virtualFolder.Locations.Length, virtualFolder.Name, libraryId);
            MediaFolderConfiguration? mediaFolderConfig = null;
            var libraryConfig = config.MediaFolders.FirstOrDefault(c => c.LibraryId == libraryId);
            foreach (var mediaFolderPath in virtualFolder.Locations) {
                if (LibraryManager.FindByPath(mediaFolderPath, true) is not Folder secondFolder)
                {
                    Logger.LogTrace("Unable to find database entry for {Path}", mediaFolderPath);
                    continue;
                }

                if (config.MediaFolders.Find(c => string.Equals(mediaFolderPath, c.MediaFolderPath) && c.LibraryId == libraryId) is { } mfc)
                {
                    Logger.LogTrace("Found existing entry for media folder at {Path} (Library={LibraryId})", mediaFolderPath, libraryId);
                    mediaFolderConfig = mfc;
                    continue;
                }

                mediaFolderConfig = await CreateConfigurationForPath(libraryId, secondFolder, libraryConfig).ConfigureAwait(false);
            }

            if (mediaFolderConfig is null)
                continue;

            var vfsPath = libraryFolder.GetVirtualRoot();
            var vfsFolderName = Path.GetFileName(vfsPath);
            var shouldAttach = config.VFS_AttachRoot && mediaFolderConfig.IsVirtualFileSystemEnabled;
            if (shouldAttach && !virtualFolder.Locations.Contains(vfsPath, Path.DirectorySeparatorChar is '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)) {
                if (!LibraryEdits.TryGetValue(libraryId, out var edits))
                    LibraryEdits[libraryId] = edits = (libraryFolder.Name, [], []);
                edits.add.Add(vfsPath);
            }

            var toRemove = virtualFolder.Locations
                .Except(shouldAttach ? [vfsPath] : [])
                .Where(location =>
                    // In case the VFS root changes.
                    (string.Equals(Path.GetFileName(location), vfsFolderName) && !string.Equals(location, vfsPath)) ||
                    // In case the libraryId changes but the root remains the same.
                    Plugin.Instance.AllVirtualRoots.Any(virtualRoot => location.StartsWith(virtualRoot, Path.DirectorySeparatorChar is '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                .ToList();
            if (toRemove.Count > 0) {
                if (!LibraryEdits.TryGetValue(libraryId, out var edits))
                    LibraryEdits[libraryId] = edits = (libraryFolder.Name, [], []);
                foreach (var location in toRemove)
                    edits.remove.Add(location);
            }
        }
    }

    private async Task<MediaFolderConfiguration> CreateConfigurationForPath(Guid libraryId, Folder mediaFolder, MediaFolderConfiguration? libraryConfig)
    {
        // Check if we should introduce the VFS for the media folder.
        var config = Plugin.Instance.Configuration;
        var mediaFolderConfig = new MediaFolderConfiguration() {
            LibraryId = libraryId,
            MediaFolderId = mediaFolder.Id,
            MediaFolderPath = mediaFolder.Path,
            IsFileEventsEnabled = libraryConfig?.IsFileEventsEnabled ?? config.SignalR_FileEvents,
            IsRefreshEventsEnabled = libraryConfig?.IsRefreshEventsEnabled ?? config.SignalR_RefreshEnabled,
            IsVirtualFileSystemEnabled = libraryConfig?.IsVirtualFileSystemEnabled ?? config.VFS_Enabled,
            LibraryFilteringMode = libraryConfig?.LibraryFilteringMode ?? config.LibraryFilteringMode,
        };

        var start = DateTime.UtcNow;
        var attempts = 0;
        if (mediaFolder.Path.StartsWith(Plugin.Instance.VirtualRoot)) {
            Logger.LogDebug("Not asking remote server because {Path} is a VFS root. (Library={LibraryId})", mediaFolder.Path, libraryId);
            mediaFolderConfig.ImportFolderId = -1;
            mediaFolderConfig.ImportFolderName = "VFS Root";
            mediaFolderConfig.ImportFolderRelativePath = string.Empty;
        }
        else {
            var samplePaths = FileSystem.GetFilePaths(mediaFolder.Path, true)
                .Where(path => NamingOptions.VideoFileExtensions.Contains(Path.GetExtension(path)))
                .Take(100)
                .ToList();

            Logger.LogDebug("Asking remote server if it knows any of the {Count} sampled files in {Path}. (Library={LibraryId})", samplePaths.Count > 100 ? 100 : samplePaths.Count, mediaFolder.Path, libraryId);
            foreach (var path in samplePaths) {
                attempts++;
                var partialPath = path[mediaFolder.Path.Length..];
                var files = await ApiClient.GetFileByPath(partialPath).ConfigureAwait(false);
                var file = files.FirstOrDefault();
                if (file is null)
                    continue;

                var fileId = file.Id.ToString();
                var fileLocations = file.Locations
                    .Where(location => location.RelativePath.EndsWith(partialPath))
                    .ToList();
                if (fileLocations.Count is 0)
                    continue;

                var fileLocation = fileLocations[0];
                mediaFolderConfig.ImportFolderId = fileLocation.ImportFolderId;
                mediaFolderConfig.ImportFolderRelativePath = fileLocation.RelativePath[..^partialPath.Length];
                break;
            }

            try {
                var importFolder = await ApiClient.GetImportFolder(mediaFolderConfig.ImportFolderId);
                if (importFolder != null)
                    mediaFolderConfig.ImportFolderName = importFolder.Name;
            }
            catch { }
        }

        // Store and log the result.
        MediaFolderChangeKeys[mediaFolder.Id] = ConstructKey(mediaFolderConfig);
        config.MediaFolders.Add(mediaFolderConfig);
        Plugin.Instance.UpdateConfiguration(config);
        if (mediaFolderConfig.IsMapped) {
            Logger.LogInformation(
                "Found a match for media folder at {Path} in {TimeSpan}. (ImportFolder={FolderId},RelativePath={RelativePath},MediaLibrary={Path},Attempts={Attempts},Library={LibraryId})",
                mediaFolder.Path,
                DateTime.UtcNow - start,
                mediaFolderConfig.ImportFolderId,
                mediaFolderConfig.ImportFolderRelativePath,
                mediaFolder.Path,
                attempts,
                libraryId
            );
        }
        else {
            Logger.LogWarning(
                "Failed to find a match for media folder at {Path} after {Amount} attempts in {TimeSpan}. (Library={LibraryId})",
                mediaFolder.Path,
                attempts,
                DateTime.UtcNow - start,
                libraryId
            );
        }

        ConfigurationAdded?.Invoke(null, new(mediaFolderConfig, mediaFolder));

        return mediaFolderConfig;
    }

    #endregion
}
