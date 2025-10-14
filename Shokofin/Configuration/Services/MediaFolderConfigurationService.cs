using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Configuration.Models;
using Shokofin.Extensions;
using Shokofin.Utils;

namespace Shokofin.Configuration;

public class MediaFolderConfigurationService {
    private readonly ILogger<MediaFolderConfigurationService> Logger;

    private readonly ILibraryManager LibraryManager;

    private readonly IFileSystem FileSystem;

    private readonly LibraryScanWatcher LibraryScanWatcher;

    private readonly ShokoIdLookup Lookup;

    private readonly UsageTracker UsageTracker;

    private readonly ShokoApiClient ApiClient;

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
        LibraryScanWatcher libraryScanWatcher,
        ShokoIdLookup lookup,
        UsageTracker usageTracker,
        ShokoApiClient apiClient
    ) {
        Logger = logger;
        LibraryManager = libraryManager;
        FileSystem = fileSystem;
        LibraryScanWatcher = libraryScanWatcher;
        Lookup = lookup;
        UsageTracker = usageTracker;
        ApiClient = apiClient;

        foreach (var mediaConfig in Plugin.Instance.Configuration.MediaFolders)
            MediaFolderChangeKeys[mediaConfig.MediaFolderId] = ConstructKey(mediaConfig);
        UsageTracker.Stalled += OnUsageTrackerStalled;
        LibraryScanWatcher.ValueChanged += OnLibraryScanValueChanged;
        LibraryManager.ItemRemoved += OnLibraryManagerItemRemoved;
        Plugin.Instance.ConfigurationChanged += OnConfigurationChanged;
    }

    ~MediaFolderConfigurationService() {
        LibraryManager.ItemRemoved -= OnLibraryManagerItemRemoved;
        Plugin.Instance.ConfigurationChanged -= OnConfigurationChanged;
        LibraryScanWatcher.ValueChanged -= OnLibraryScanValueChanged;
        UsageTracker.Stalled -= OnUsageTrackerStalled;
        MediaFolderChangeKeys.Clear();
        LockObj.Dispose();
    }

    #region Changes Tracking

    private void OnLibraryScanValueChanged(object? sender, bool isRunning) {
        if (isRunning)
            return;

        Task.Run(() => EditLibraries(true));
    }

    private void OnUsageTrackerStalled(object? sender, EventArgs eventArgs) {
        Task.Run(() => EditLibraries(false));
    }

    private async Task EditLibraries(bool shouldScheduleLibraryScan) {
        await LockObj.WaitAsync().ConfigureAwait(false);
        try {
            ShouldGenerateAllConfigurations = true;

            if (LibraryEdits.Count is 0)
                return;

            var libraryEdits = LibraryEdits.ToList();
            LibraryEdits.Clear();
            foreach (var (libraryId, (libraryName, add, remove)) in libraryEdits) {
                foreach (var vfsPath in add) {
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
                await LibraryManager.ValidateMediaLibrary(new Progress<double>(), CancellationToken.None).ConfigureAwait(false);
        }
        finally {
            LockObj.Release();
        }
    }

    private static string ConstructKey(MediaFolderConfiguration config)
        => $"IsMapped={config.IsMapped},IsFileEventsEnabled={config.IsFileEventsEnabled},IsRefreshEventsEnabled={config.IsRefreshEventsEnabled},LibraryOperationMode={config.LibraryOperationMode}";

    private void OnConfigurationChanged(object? sender, PluginConfiguration config) {
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

    private async void OnLibraryManagerItemRemoved(object? sender, ItemChangeEventArgs e) {
        var root = LibraryManager.RootFolder;
        if (e.Item != null && root != null && e.Item != root && e.Item is Folder folder && folder.ParentId == Guid.Empty  && !string.IsNullOrEmpty(folder.Path) && !folder.Path.StartsWith(root.Path)) {
            await LockObj.WaitAsync().ConfigureAwait(false);
            try {
                var virtualFolders = LibraryManager.GetVirtualFolders();
                var virtualFolderIds = virtualFolders
                    .Select(virtualFolder => string.IsNullOrEmpty(virtualFolder.ItemId) ? Guid.Empty : Guid.Parse(virtualFolder.ItemId))
                    .Except([Guid.Empty])
                    .ToList();
                var mediaFolderConfigs = Plugin.Instance.Configuration.MediaFolders
                    .Where(c => c.MediaFolderId == folder.Id && !virtualFolderIds.Contains(c.LibraryId))
                    .ToList();
                foreach (var mediaFolderConfig in mediaFolderConfigs) {
                    Logger.LogDebug(
                        "Removing stored configuration for folder at {Path} (ManagedFolder={ManagedFolderId},RelativePath={RelativePath})",
                        folder.Path,
                        mediaFolderConfig.ManagedFolderId,
                        mediaFolderConfig.ManagedFolderRelativePath
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

    public async Task<IReadOnlyList<(string vfsPath, CollectionType? collectionType, IReadOnlyList<MediaFolderConfiguration> mediaList)>> GetAvailableMediaFoldersForLibraries(Func<MediaFolderConfiguration, bool>? filter = null) {
        await LockObj.WaitAsync().ConfigureAwait(false);
        try {
            var virtualFolders = LibraryManager.GetVirtualFolders();
            if (ShouldGenerateAllConfigurations) {
                ShouldGenerateAllConfigurations = false;
                await GenerateAllConfigurations(virtualFolders).ConfigureAwait(false);
            }

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
                    collectionType: tuple.virtualFolder!.CollectionType.ConvertToCollectionType(),
                    tuple.mediaList
                ))
                .Where(tuple => !string.IsNullOrEmpty(tuple.vfsPath))
                .ToList();
        }
        finally {
            LockObj.Release();
        }
    }

    public async Task<(MediaFolderConfiguration? vfsRootConfig, IReadOnlyList<MediaFolderConfiguration> mediaList, bool skipGeneration)> GetMediaFoldersForLibraryInVFS(Folder mediaFolder, CollectionType? collectionType, Func<MediaFolderConfiguration, bool>? filter = null) {
        var mediaFolderConfig = await GetOrCreateConfigurationForMediaFolder(mediaFolder, collectionType).ConfigureAwait(false);
        await LockObj.WaitAsync().ConfigureAwait(false);
        try {
            var skipGeneration = LibraryEdits.Count is > 0 && LibraryManager.IsScanRunning;
            if (LibraryManager.GetItemById(mediaFolderConfig.LibraryId) is not Folder libraryFolder)
                return (null, [], skipGeneration);

            var virtualFolder = LibraryManager.GetVirtualFolders()
                .FirstOrDefault(folder => Guid.TryParse(folder.ItemId, out var guid) && guid == mediaFolderConfig.LibraryId);
            if (virtualFolder is null || virtualFolder.Locations.Length is 0)
                return (null, [], skipGeneration);

            var vfsPath = libraryFolder.GetVirtualRoot();
            var vfsRootConfig = Plugin.Instance.Configuration.MediaFolders.FirstOrDefault(config => config.IsVirtualRoot && config.LibraryId == mediaFolderConfig.LibraryId);
            if (vfsRootConfig is null || vfsPath is null || vfsRootConfig.MediaFolderPath != vfsPath)
                return (null, [], skipGeneration);

            var mediaFolders = Plugin.Instance.Configuration.MediaFolders
                .Where(config => config.IsMapped && !config.IsVirtualRoot && config.LibraryId == mediaFolderConfig.LibraryId && (filter is null || filter(config)) && LibraryManager.GetItemById(config.MediaFolderId) is Folder)
                .ToList();
            return (vfsRootConfig, mediaFolders, skipGeneration);
        }
        finally {
            LockObj.Release();
        }
    }

    public async Task<MediaFolderConfiguration> GetOrCreateConfigurationForMediaFolder(Folder mediaFolder, CollectionType? collectionType = CollectionType.unknown) {
        await LockObj.WaitAsync().ConfigureAwait(false);
        try {
            var allVirtualFolders = LibraryManager.GetVirtualFolders();
            if (allVirtualFolders.FirstOrDefault(p => p.Locations.Contains(mediaFolder.Path) && (collectionType is CollectionType.unknown || p.CollectionType.ConvertToCollectionType() == collectionType)) is not { } library)
                throw new Exception($"Unable to find any library to use for media folder \"{mediaFolder.Path}\"");

            if (string.IsNullOrEmpty(library.ItemId) || !Guid.TryParse(library.ItemId, out var libraryId))
                throw new Exception($"Unable to parse library id for library \"{library.Name}\" to use for media folder \"{mediaFolder.Path}\". This is not a plugin bug, but the media folder is missing from the default view in Jellyfin.");

            if (ShouldGenerateAllConfigurations) {
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

    private async Task GenerateAllConfigurations(List<VirtualFolderInfo> allVirtualFolders) {
        var filteredVirtualFolders = allVirtualFolders
            .Where(virtualFolder => {
                if (virtualFolder is not { ItemId: not null, LibraryOptions: { } }) {
                    Logger.LogWarning("Skipping virtual folder {Name} because it has no ItemId or LibraryOptions.", virtualFolder.Name);
                    return false;
                }

                return virtualFolder.CollectionType.ConvertToCollectionType() is null or CollectionType.movies or CollectionType.tvshows &&
                    Lookup.IsEnabledForLibraryOptions(virtualFolder.LibraryOptions);
            })
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
                if (LibraryManager.FindByPath(mediaFolderPath, true) is not Folder secondFolder) {
                    Logger.LogTrace("Unable to find database entry for {Path} (Library={LibraryId})", mediaFolderPath, libraryId);
                    continue;
                }

                if (config.MediaFolders.Find(c => string.Equals(mediaFolderPath, c.MediaFolderPath) && c.LibraryId == libraryId) is { } mfc) {
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
            var shouldAttach = mediaFolderConfig.IsVirtualFileSystemEnabled;
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

        var mediaFoldersToRemove = config.MediaFolders
            .Where(c => !filteredVirtualFolders.Any(v => Guid.Parse(v.ItemId) == c.LibraryId))
            .ToList();
        Logger.LogDebug("Found {Count} out of {TotalCount} media folders to remove.", mediaFoldersToRemove.Count, config.MediaFolders.Count);
        foreach (var mediaFolder in mediaFoldersToRemove) {
            Logger.LogTrace("Removing config for media folder at path {Path} (Library={LibraryId})", mediaFolder.MediaFolderPath, mediaFolder.LibraryId);
            config.MediaFolders.Remove(mediaFolder);
        }
    }

    private async Task<MediaFolderConfiguration> CreateConfigurationForPath(Guid libraryId, Folder mediaFolder, MediaFolderConfiguration? libraryConfig) {
        // Check if we should introduce the VFS for the media folder.
        var config = Plugin.Instance.Configuration;
        var mediaFolderConfig = new MediaFolderConfiguration() {
            LibraryId = libraryId,
            MediaFolderId = mediaFolder.Id,
            MediaFolderPath = mediaFolder.Path,
            IsFileEventsEnabled = libraryConfig?.IsFileEventsEnabled ?? config.SignalR_FileEvents,
            IsRefreshEventsEnabled = libraryConfig?.IsRefreshEventsEnabled ?? config.SignalR_RefreshEnabled,
            LibraryOperationMode = libraryConfig?.LibraryOperationMode ?? config.DefaultLibraryOperationMode,
            IterativeVfsGeneration_Enabled = libraryConfig?.IterativeVfsGeneration_Enabled ?? config.VFS_IterativeGenerationEnabled,
            IterativeVfsGeneration_MaxCount = libraryConfig?.IterativeVfsGeneration_MaxCount ?? config.VFS_IterativeGenerationMaxCount,
        };

        var start = DateTime.UtcNow;
        var attempts = 0;
        if (mediaFolder.Path.StartsWith(Plugin.Instance.VirtualRoot)) {
            Logger.LogDebug("Not asking remote server because {Path} is a VFS root. (Library={LibraryId})", mediaFolder.Path, libraryId);
            mediaFolderConfig.ManagedFolderId = -1;
            mediaFolderConfig.ManagedFolderName = "VFS Root";
            mediaFolderConfig.ManagedFolderRelativePath = string.Empty;
        }
        else {
            var foundLocations = new List<(int, string)>();
            var samplePaths = GetSamplePaths(mediaFolder.Path).ToList();

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
                foundLocations.Add((fileLocation.ManagedFolderId, fileLocation.RelativePath[..^partialPath.Length]));
            }

            if (foundLocations.Count > 0) {
                var groupedLocations = foundLocations
                    .GroupBy(x => x)
                    .ToDictionary(x => x.Key, x => x.Count());
                foreach (var ((managedFolderId, relativePath), count) in groupedLocations) {
                    Logger.LogDebug("Found {Count} hits for managed folder {Id} at relative path {RelativePath}. (Library={LibraryId})", count, managedFolderId, relativePath, libraryId);
                }
                (mediaFolderConfig.ManagedFolderId, mediaFolderConfig.ManagedFolderRelativePath) = groupedLocations
                    .MaxBy(x => x.Value)!
                    .Key;
            }

            try {
                var managedFolder = await ApiClient.GetManagedFolder(mediaFolderConfig.ManagedFolderId).ConfigureAwait(false);
                if (managedFolder != null)
                    mediaFolderConfig.ManagedFolderName = managedFolder.Name;
            }
            catch { }
        }

        // Store and log the result.
        MediaFolderChangeKeys[mediaFolder.Id] = ConstructKey(mediaFolderConfig);
        config.MediaFolders.Add(mediaFolderConfig);
        Plugin.Instance.UpdateConfiguration(config);
        if (mediaFolderConfig.IsMapped) {
            Logger.LogInformation(
                "Found a match for media folder at {Path} in {TimeSpan}. (ManagedFolder={FolderId},RelativePath={RelativePath},MediaLibrary={Path},Attempts={Attempts},Library={LibraryId})",
                mediaFolder.Path,
                DateTime.UtcNow - start,
                mediaFolderConfig.ManagedFolderId,
                mediaFolderConfig.ManagedFolderRelativePath,
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

    /// <summary>
    /// Max number of sample paths to return. We use an odd number as a tie
    /// breaker in case of multiple different matches.
    /// </summary>
    private const int MaxSamplePaths = 101;

    /// <summary>
    /// Gets the sample paths for the given media folder.
    /// </summary>
    /// <param name="mediaFolder">The media folder to get the sample paths
    /// for.</param>
    /// <returns>The sample paths for the given media folder.</returns> 
    private IEnumerable<string> GetSamplePaths(string mediaFolder) {
        var count = 0;
        var rootFiles = FileSystem.GetFilePaths(mediaFolder, false);
        foreach (var filePath in rootFiles) {
            if (IgnorePatterns.ShouldIgnore(filePath))
                continue;

            yield return filePath;

            if (++count == MaxSamplePaths)
                yield break;
        }

        var rootFolders = FileSystem.GetDirectoryPaths(mediaFolder, false);
        foreach (var directoryPath in rootFolders) {
            if (IgnorePatterns.ShouldIgnore(directoryPath))
                continue;

            var files = FileSystem.GetFilePaths(directoryPath, true);
            foreach (var filePath in files) {
                if (IgnorePatterns.ShouldIgnore(filePath))
                    continue;

                yield return filePath;

                if (++count == MaxSamplePaths)
                    yield break;
            }
        }
    }

    #endregion
}
