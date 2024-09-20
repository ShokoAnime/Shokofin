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

    private bool ShouldGenerateAllConfigurations = true;

    private readonly object LockObj = new();

    public event EventHandler<MediaConfigurationChangedEventArgs>? ConfigurationAdded;

    public event EventHandler<MediaConfigurationChangedEventArgs>? ConfigurationUpdated;

    public event EventHandler<MediaConfigurationChangedEventArgs>? ConfigurationRemoved;
    
    public readonly Dictionary<Guid, (string libraryName, HashSet<string> add, HashSet<string> remove)> LibraryEdits = [];

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
    }

    #region Changes Tracking

    private void OnLibraryScanValueChanged(object? sender, bool isRunning)
    {
        if (isRunning)
            return;

        Task.Run(EditLibraries);
    }

    private void OnUsageTrackerStalled(object? sender, EventArgs eventArgs)
    {
        Task.Run(EditLibraries);
    }

    private void EditLibraries()
    {
        lock (LockObj) {
            if (LibraryEdits.Count is 0)
                return;

            ShouldGenerateAllConfigurations = true;
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
            LibraryManager.ValidateMediaLibrary(new Progress<double>(), CancellationToken.None);
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

    private void OnLibraryManagerItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        var root = LibraryManager.RootFolder;
        if (e.Item != null && root != null && e.Item != root && e.Item is Folder folder && folder.ParentId == Guid.Empty  && !string.IsNullOrEmpty(folder.Path) && !folder.Path.StartsWith(root.Path)) {
            lock (LockObj) {
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
        }
    }

    #endregion

    #region Media Folder Mapping

    public IReadOnlyList<(string vfsPath, string mainMediaFolderPath, CollectionType? collectionType, IReadOnlyList<MediaFolderConfiguration> mediaList)> GetAvailableMediaFoldersForLibraries(Func<MediaFolderConfiguration, bool>? filter = null)
    {
        lock (LockObj) {
            var virtualFolders = LibraryManager.GetVirtualFolders();
            return Plugin.Instance.Configuration.MediaFolders
                .Where(config => config.IsMapped && !config.IsVirtualRoot && (filter is null || filter(config)) && LibraryManager.GetItemById(config.MediaFolderId) is Folder)
                .GroupBy(config => config.LibraryId)
                .Select(groupBy => (
                    libraryFolder: LibraryManager.GetItemById(groupBy.Key) as Folder,
                    virtualFolder: virtualFolders.FirstOrDefault(folder => Guid.TryParse(folder.ItemId, out var guid) && guid == groupBy.Key),
                    mediaList: groupBy
                        .Where(config => LibraryManager.GetItemById(config.MediaFolderId) is Folder)
                        .ToList() as IReadOnlyList<MediaFolderConfiguration>
                ))
                .Where(tuple => tuple.libraryFolder is not null && tuple.virtualFolder is not null && tuple.virtualFolder.Locations.Length is > 0 && tuple.mediaList.Count is > 0)
                .Select(tuple => (
                    vfsPath: tuple.libraryFolder!.GetVirtualRoot(),
                    mainMediaFolderPath: tuple.virtualFolder!.Locations.FirstOrDefault(a => DirectoryService.IsAccessible(a)) ?? string.Empty,
                    collectionType: LibraryManager.GetConfiguredContentType(tuple.libraryFolder!),
                    tuple.mediaList
                ))
                .Where(tuple => !string.IsNullOrEmpty(tuple.vfsPath) && !string.IsNullOrEmpty(tuple.mainMediaFolderPath))
                .ToList();
        }
    }

    public (string vfsPath, string mainMediaFolderPath, IReadOnlyList<MediaFolderConfiguration> mediaList) GetAvailableMediaFoldersForLibrary(Folder mediaFolder, CollectionType? collectionType, Func<MediaFolderConfiguration, bool>? filter = null)
    {
        var attachRoot = Plugin.Instance.Configuration.VFS_AttachRoot;
        var mediaFolderConfig = GetOrCreateConfigurationForMediaFolder(mediaFolder, collectionType);
        lock (LockObj) {
            if (LibraryManager.GetItemById(mediaFolderConfig.LibraryId) is not Folder libraryFolder)
                return (string.Empty, string.Empty, []);
            var virtualFolder = LibraryManager.GetVirtualFolders()
                .FirstOrDefault(folder => Guid.TryParse(folder.ItemId, out var guid) && guid == mediaFolderConfig.LibraryId);
            if (virtualFolder is null || virtualFolder.Locations.Length is 0)
                return (string.Empty, string.Empty, []);

            var vfsPath = libraryFolder.GetVirtualRoot();
            var mediaFolders = Plugin.Instance.Configuration.MediaFolders
                .Where(config => config.IsMapped && !config.IsVirtualRoot && config.LibraryId == mediaFolderConfig.LibraryId && (filter is null || filter(config)) && LibraryManager.GetItemById(config.MediaFolderId) is Folder)
                .ToList();
            if (attachRoot && mediaFolderConfig.IsVirtualFileSystemEnabled)
                return (vfsPath, vfsPath, mediaFolders);

            return (
                libraryFolder.GetVirtualRoot(),
                virtualFolder.Locations.FirstOrDefault(a => DirectoryService.IsAccessible(a)) ?? string.Empty,
                mediaFolders
            );
        }
    }

    public MediaFolderConfiguration GetOrCreateConfigurationForMediaFolder(Folder mediaFolder, CollectionType? collectionType = CollectionType.unknown)
    {
        lock (LockObj) {
            var allVirtualFolders = LibraryManager.GetVirtualFolders();
            if (allVirtualFolders.FirstOrDefault(p => p.Locations.Contains(mediaFolder.Path) && (collectionType is CollectionType.unknown || p.CollectionType.ConvertToCollectionType() == collectionType)) is not { } library || !Guid.TryParse(library.ItemId, out var libraryId))
                throw new Exception($"Unable to find library to use for media folder \"{mediaFolder.Path}\"");

            var config = Plugin.Instance.Configuration;
            var attachRoot = config.VFS_AttachRoot;
            var libraryConfig = config.MediaFolders.FirstOrDefault(c => c.LibraryId == libraryId);
            var mediaFolderConfig = config.MediaFolders.FirstOrDefault(c => c.MediaFolderId == mediaFolder.Id && c.LibraryId == libraryId) ??
                CreateConfigurationForPath(libraryId, mediaFolder, libraryConfig).ConfigureAwait(false).GetAwaiter().GetResult();

            GenerateAllConfigurations(allVirtualFolders);

            return mediaFolderConfig;
        }
    }

    private void GenerateAllConfigurations(List<VirtualFolderInfo> allVirtualFolders)
    {
        if (!ShouldGenerateAllConfigurations)
            return;
        ShouldGenerateAllConfigurations = false;

        var filteredVirtualFolders = allVirtualFolders
            .Where(virtualFolder => 
                virtualFolder.CollectionType.ConvertToCollectionType() is null or CollectionType.movies or CollectionType.tvshows &&
                Lookup.IsEnabledForLibraryOptions(virtualFolder.LibraryOptions, out _)
            )
            .ToList();
        var config = Plugin.Instance.Configuration;
        var attachRoot = config.VFS_AttachRoot;
        foreach (var virtualFolder in filteredVirtualFolders) {
            if (!Guid.TryParse(virtualFolder.ItemId, out var libraryId) || LibraryManager.GetItemById(libraryId) is not Folder libraryFolder)
                throw new Exception($"Unable to find virtual folder \"{virtualFolder.Name}\"");

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
                    mediaFolderConfig = mfc;
                    continue;
                }

                mediaFolderConfig = CreateConfigurationForPath(libraryId, secondFolder, libraryConfig).ConfigureAwait(false).GetAwaiter().GetResult();
            }

            if (!attachRoot || !(mediaFolderConfig?.IsVirtualFileSystemEnabled ?? false))
                continue;

            var vfsPath = libraryFolder.GetVirtualRoot();
            if (!virtualFolder.Locations.Contains(vfsPath, Path.DirectorySeparatorChar is '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)) {
                if (!LibraryEdits.TryGetValue(libraryId, out var edits))
                    LibraryEdits[libraryId] = edits = (libraryFolder.Name, [], []);
                edits.add.Add(vfsPath);
            }

            var virtualRoot = Plugin.Instance.VirtualRoot;
            var toRemove = virtualFolder.Locations
                .Except([vfsPath])
                .Where(location => location.StartsWith(virtualRoot, Path.DirectorySeparatorChar is '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
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
            Logger.LogDebug("Not asking remote server because {Path} is a VFS root.", mediaFolder.Path);
            mediaFolderConfig.ImportFolderId = -1;
            mediaFolderConfig.ImportFolderName = "VFS Root";
            mediaFolderConfig.ImportFolderRelativePath = string.Empty;
        }
        else {
            var samplePaths = FileSystem.GetFilePaths(mediaFolder.Path, true)
                .Where(path => NamingOptions.VideoFileExtensions.Contains(Path.GetExtension(path)))
                .Take(100)
                .ToList();

            Logger.LogDebug("Asking remote server if it knows any of the {Count} sampled files in {Path}.", samplePaths.Count > 100 ? 100 : samplePaths.Count, mediaFolder.Path);
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

        ConfigurationAdded?.Invoke(null, new(mediaFolderConfig, mediaFolder));

        return mediaFolderConfig;
    }

    #endregion
}
