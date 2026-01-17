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

    private readonly UsageTracker UsageTracker;

    private readonly ShokoApiClient ApiClient;

    private readonly Dictionary<Guid, int> LibraryChangeTracker = [];

    private readonly Dictionary<Guid, (string libraryName, HashSet<string> add, HashSet<string> remove)> LibraryEdits = [];

    private List<VirtualFolderInfo>? CachedVirtualFolders = null;

    private bool ShouldGenerateAllConfigurations = true;

    private readonly SemaphoreSlim LockObj = new(1, 1);

    public event EventHandler<LibraryConfigurationChangedEventArgs>? LibraryConfigurationAdded;

    public event EventHandler<LibraryConfigurationChangedEventArgs>? LibraryConfigurationChanged;

    public event EventHandler<LibraryConfigurationChangedEventArgs>? LibraryConfigurationRemoved;

    public event EventHandler<MediaConfigurationChangedEventArgs>? MediaFolderConfigurationAdded;

    public event EventHandler<MediaConfigurationChangedEventArgs>? MediaFolderConfigurationRemoved;

    public MediaFolderConfigurationService(
        ILogger<MediaFolderConfigurationService> logger,
        ILibraryManager libraryManager,
        IFileSystem fileSystem,
        LibraryScanWatcher libraryScanWatcher,
        UsageTracker usageTracker,
        ShokoApiClient apiClient
    ) {
        Logger = logger;
        LibraryManager = libraryManager;
        FileSystem = fileSystem;
        LibraryScanWatcher = libraryScanWatcher;
        UsageTracker = usageTracker;
        ApiClient = apiClient;

        foreach (var libraryConfig in Plugin.Instance.Configuration.Libraries)
            LibraryChangeTracker[libraryConfig.Id] = ConstructKey(libraryConfig);
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
        LibraryChangeTracker.Clear();
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
            CachedVirtualFolders = null;
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

    private int ConstructKey(LibraryConfiguration config)
        => HashCode.Combine(config.Id, config.IsFileEventsEnabled, config.IsRefreshEventsEnabled, config.LibraryOperationMode, config.IterativeVfsGeneration_Enabled);

    private void OnConfigurationChanged(object? sender, PluginConfiguration config) {
        foreach (var libraryConfig in config.Libraries) {
            var currentKey = ConstructKey(libraryConfig);
            if (LibraryChangeTracker.TryGetValue(libraryConfig.Id, out var previousKey) && previousKey != currentKey) {
                LibraryChangeTracker[libraryConfig.Id] = currentKey;
                LibraryConfigurationChanged?.Invoke(sender, new(libraryConfig, libraryConfig.MediaFolders));
            }
        }
    }

    private async void OnLibraryManagerItemRemoved(object? sender, ItemChangeEventArgs e) {
        var root = LibraryManager.RootFolder;
        if (e.Item != null && root != null && e.Item != root && e.Item is Folder folder && folder.ParentId == Guid.Empty  && !string.IsNullOrEmpty(folder.Path) && !folder.Path.StartsWith(root.Path)) {
            await LockObj.WaitAsync().ConfigureAwait(false);
            try {
                ShouldGenerateAllConfigurations = false;
                await GenerateAllConfigurations(GetVirtualFolders()).ConfigureAwait(false);
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
            var virtualFolders = GetVirtualFolders();
            if (ShouldGenerateAllConfigurations) {
                ShouldGenerateAllConfigurations = false;
                await GenerateAllConfigurations(virtualFolders).ConfigureAwait(false);
            }

            return Plugin.Instance.Configuration.LibraryFolders
                .Where(config => config.IsMapped && (filter is null || filter(config)))
                .GroupBy(config => config.LibraryId)
                .Select(groupBy => (
                    libraryConfig: groupBy.First().Library,
                    virtualFolder: virtualFolders.FirstOrDefault(folder => Guid.TryParse(folder.ItemId, out var guid) && guid == groupBy.Key),
                    mediaList: groupBy.ToList() as IReadOnlyList<MediaFolderConfiguration>
                ))
                .Where(tuple => tuple.virtualFolder is not null && tuple.virtualFolder.Locations.Length is > 0 && tuple.mediaList.Count is > 0)
                .Select(tuple => (
                    vfsPath: tuple.libraryConfig.VirtualRoot,
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

    public async Task<(LibraryConfiguration? vfsRootConfig, IReadOnlyList<MediaFolderConfiguration> mediaList, bool skipGeneration)> GetMediaFoldersForLibraryInVFS(Folder mediaFolder, CollectionType? collectionType) {
        var (libraryConfig, mediaFolderConfig) = await GetOrCreateConfigurationForMediaFolder(mediaFolder, collectionType).ConfigureAwait(false);
        await LockObj.WaitAsync().ConfigureAwait(false);
        try {
            var skipGeneration = LibraryEdits.Count is > 0 && LibraryManager.IsScanRunning;
            if (libraryConfig is null || !libraryConfig.IsVirtualFileSystemEnabled || mediaFolderConfig is not null)
                return (null, [], skipGeneration);
            var mediaFolders = libraryConfig.MediaFolders
                .Where(config => config.IsMapped)
                .ToList();
            return (libraryConfig, mediaFolders, skipGeneration);
        }
        finally {
            LockObj.Release();
        }
    }

    public async Task<(LibraryConfiguration? libraryConfiguration, MediaFolderConfiguration? mediaFolderConfiguration)> GetOrCreateConfigurationForMediaFolder(Folder mediaFolder, CollectionType? collectionType = CollectionType.unknown) {
        await LockObj.WaitAsync().ConfigureAwait(false);
        try {
            var allVirtualFolders = GetVirtualFolders();
            if (allVirtualFolders.FirstOrDefault(p => p.Locations.Contains(mediaFolder.Path) && (collectionType is CollectionType.unknown || p.CollectionType.ConvertToCollectionType() == collectionType)) is not { } library)
                throw new Exception($"Unable to find any library to use for media folder \"{mediaFolder.Path}\"");

            if (string.IsNullOrEmpty(library.ItemId) || !Guid.TryParse(library.ItemId, out var libraryId))
                throw new Exception($"Unable to parse library id for library \"{library.Name}\" to use for media folder \"{mediaFolder.Path}\". This is not a plugin bug, but the media folder is missing from the default view in Jellyfin.");

            if (ShouldGenerateAllConfigurations) {
                ShouldGenerateAllConfigurations = false;
                await GenerateAllConfigurations(allVirtualFolders).ConfigureAwait(false);
            }

            if (Plugin.Instance.Configuration.Libraries.FirstOrDefault(lib => lib.IsVirtualFileSystemEnabled && lib.VirtualRoot == mediaFolder.Path) is { } libraryConfig) {
                return (libraryConfig, null);
            }

            foreach (var mediaFolderConfig in Plugin.Instance.Configuration.LibraryFolders.Where(mf => mf.Path == mediaFolder.Path).ToList()) {
                return (mediaFolderConfig.Library, mediaFolderConfig);
            }
            return (null, null);
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
                    ShokoIdLookup.IsEnabledForLibraryOptions(virtualFolder.LibraryOptions);
            })
            .ToList();
        Logger.LogDebug("Found {Count} out of {TotalCount} libraries to check media folder configurations for.", filteredVirtualFolders.Count, allVirtualFolders.Count);
        var shouldSaveConfig = false;
        var newLibraryConfigList = new List<LibraryConfiguration>();
        var oldLibraryConfigList = new List<LibraryConfiguration>();
        var newFolderConfigList = new List<(LibraryConfiguration libraryConfiguration, MediaFolderConfiguration mediaFolderConfiguration)>();
        var oldFolderConfigList = new List<(LibraryConfiguration libraryConfiguration, MediaFolderConfiguration mediaFolderConfiguration)>();
        var librariesToKeep = new HashSet<Guid>();
        var config = Plugin.Instance.Configuration;
        foreach (var virtualFolder in filteredVirtualFolders) {
            if (!Guid.TryParse(virtualFolder.ItemId, out var libraryId) || LibraryManager.GetItemById(libraryId) is not Folder libraryFolder) {
                Logger.LogWarning("Unable to find virtual folder with name: {VirtualFolderName}", virtualFolder.Name);
                continue;
            }
            if (!librariesToKeep.Add(libraryId)) {
                Logger.LogTrace("Skipping library {LibraryName} because it has already been processed. (Library={LibraryId})", libraryFolder.Name, libraryId);
                continue;
            }
            if (config.Libraries.FirstOrDefault(c => c.Id == libraryId) is not { } libraryConfig) {
                libraryConfig = new() {
                    Id = libraryId,
                    Name = libraryFolder.Name,
                    IsFileEventsEnabled = config.SignalR_FileEvents,
                    IsRefreshEventsEnabled = config.SignalR_RefreshEnabled,
                    LibraryOperationMode = config.DefaultLibraryOperationMode,
                    IterativeVfsGeneration_Enabled = config.VFS_IterativeGenerationEnabled,
                    IterativeVfsGeneration_MaxCount = config.VFS_IterativeGenerationMaxCount,
                };
                config.Libraries.Add(libraryConfig);
                newLibraryConfigList.Add(libraryConfig);
                shouldSaveConfig = true;
            }
            if (!string.Equals(libraryConfig.Name, libraryFolder.Name, StringComparison.Ordinal)) {
                libraryConfig.Name = libraryFolder.Name;
                shouldSaveConfig = true;
            }
            Logger.LogDebug("Checking {MediaFolderCount} media folders for library {LibraryName}. (Library={LibraryId})", virtualFolder.Locations.Length, virtualFolder.Name, libraryId);
            foreach (var mediaFolderPath in virtualFolder.Locations) {
                // Remove empty/invalid media folders.
                if (string.IsNullOrEmpty(mediaFolderPath)) {
                    RemoveFromLibrary(libraryConfig, string.Empty);
                    continue;
                }
                // Add or remove the VFS root as a media folder as needed.
                if (mediaFolderPath == libraryConfig.VirtualRoot) {
                    if (!libraryConfig.IsVirtualFileSystemEnabled) {
                        RemoveFromLibrary(libraryConfig, mediaFolderPath);
                    }
                    continue;
                }
                // Remove stale VFS roots.
                if (Plugin.Instance.AllVirtualRoots.Any(mediaFolderPath.StartsWith)) {
                    RemoveFromLibrary(libraryConfig, mediaFolderPath);
                    continue;
                }
                // Add config if needed.
                if (libraryConfig.MediaFolders.FirstOrDefault(mf => mf.Path == mediaFolderPath) is not { } mediaFolderConfig) {
                    mediaFolderConfig = await CreateConfigurationForPath(libraryId, mediaFolderPath).ConfigureAwait(false);
                    config.LibraryFolders.Add(mediaFolderConfig);
                    newFolderConfigList.Add((libraryConfig, mediaFolderConfig));
                    shouldSaveConfig = true;
                }
                // Remove folder from library if VFS is enabled and it's not ignored.
                if (libraryConfig.IsVirtualFileSystemEnabled && !mediaFolderConfig.IsIgnored) {
                    RemoveFromLibrary(libraryConfig, mediaFolderPath);
                }
            }
            if (libraryConfig.IsVirtualFileSystemEnabled && !virtualFolder.Locations.Contains(libraryConfig.VirtualRoot)) {
                AddToLibrary(libraryConfig, libraryConfig.VirtualRoot);
            }
            foreach (var mediaFolderConfig in libraryConfig.MediaFolders) {
                if (!libraryConfig.IsVirtualFileSystemEnabled) {
                    // We have disabled the VFS and need to re-add the plugin managed media folders again.
                    var ignoredFolders = libraryConfig.MediaFolders.Where(mf => mf.IsIgnored).Select(mf => mf.Path).ToHashSet();
                    if (virtualFolder.Locations.Length == ignoredFolders.Count + 1 && virtualFolder.Locations.All(l => l == libraryConfig.VirtualRoot || ignoredFolders.Contains(l))) {
                        AddToLibrary(libraryConfig, mediaFolderConfig.Path);
                    }
                    // The VFS is disabled, and we have a mapping for a media folder which is not linked to the library,
                    // so we need to remove it.
                    else if (!virtualFolder.Locations.Contains(mediaFolderConfig.Path)) {
                        config.LibraryFolders.Remove(mediaFolderConfig);
                        oldFolderConfigList.Add((libraryConfig, mediaFolderConfig));
                        shouldSaveConfig = true;
                        continue;
                    }
                }
                // If we have an ignored media folder that's not added to the library when the VFS is enabled,
                // then we need to add it.
                else if (mediaFolderConfig.IsIgnored && !virtualFolder.Locations.Contains(mediaFolderConfig.Path)) {
                    AddToLibrary(libraryConfig, mediaFolderConfig.Path);
                }
                // Refresh config if needed.
                if (!mediaFolderConfig.IsIgnored && mediaFolderConfig.NeedsRefresh) {
                    var newMediaFolderConfig = await CreateConfigurationForPath(libraryId, mediaFolderConfig.Path).ConfigureAwait(false);
                    mediaFolderConfig.MergeWith(newMediaFolderConfig);
                    mediaFolderConfig.NeedsRefresh = false;
                    shouldSaveConfig = true;
                }
            }
        }
        var librariesToRemove = config.Libraries
            .ExceptBy(librariesToKeep, c => c.Id)
            .ToList();
        var mediaFoldersToRemove = config.LibraryFolders
            .ExceptBy(librariesToKeep, c => c.LibraryId)
            .ToList();
        foreach (var mediaFolder in mediaFoldersToRemove) {
            Logger.LogTrace("Removing config for media folder at path {Path} (Library={LibraryId})", mediaFolder.Path, mediaFolder.LibraryId);
            config.LibraryFolders.Remove(mediaFolder);
            var libraryConfig = config.Libraries.FirstOrDefault(c => c.Id == mediaFolder.LibraryId);
            if (libraryConfig is not null)
                oldFolderConfigList.Add((libraryConfig, mediaFolder));
            shouldSaveConfig = true;
        }
        foreach (var library in librariesToRemove) {
            Logger.LogTrace("Removing config for library {LibraryName} (Library={LibraryId})", library.Name, library.Id);
            config.Libraries.Remove(library);
            oldLibraryConfigList.Add(library);
            shouldSaveConfig = true;
        }
        Logger.LogDebug("Removed {Count} libraries and {MediaFolderCount} media folders from configuration.", librariesToRemove.Count, mediaFoldersToRemove.Count);
        if (shouldSaveConfig)
            Plugin.Instance.SaveConfiguration(config);
        foreach (var libraryConfig in newLibraryConfigList) {
            LibraryChangeTracker[libraryConfig.Id] = ConstructKey(libraryConfig);
            try {
                LibraryConfigurationAdded?.Invoke(null, new(libraryConfig, libraryConfig.MediaFolders));
            }
            catch { }
        }
        foreach (var (libraryConfig, mediaFolderConfig) in newFolderConfigList.ExceptBy(newLibraryConfigList, c => c.libraryConfiguration)) {
            try {
                MediaFolderConfigurationAdded?.Invoke(null, new(libraryConfig, mediaFolderConfig));
            }
            catch { }
        }
        foreach (var (libraryConfig, mediaFolderConfig) in oldFolderConfigList.ExceptBy(oldLibraryConfigList, c => c.libraryConfiguration)) {
            try {
                MediaFolderConfigurationRemoved?.Invoke(null, new(libraryConfig, mediaFolderConfig));
            }
            catch { }
        }
        foreach (var libraryConfig in oldLibraryConfigList) {
            LibraryChangeTracker.Remove(libraryConfig.Id);
            try {
                var mediaFolders = mediaFoldersToRemove
                    .Where(mf => mf.LibraryId == libraryConfig.Id)
                    .ToList();
                LibraryConfigurationRemoved?.Invoke(null, new(libraryConfig, mediaFolders));
            }
            catch { }
        }
    }

    private void AddToLibrary(LibraryConfiguration config, string path) {
        if (!LibraryEdits.TryGetValue(config.Id, out var edits))
            LibraryEdits[config.Id] = edits = (config.Name, [], []);
        edits.add.Add(path);
    }

    private void RemoveFromLibrary(LibraryConfiguration config, string path) {
        if (!LibraryEdits.TryGetValue(config.Id, out var edits))
            LibraryEdits[config.Id] = edits = (config.Name, [], []);
        edits.remove.Add(path);
    }

    private async Task<MediaFolderConfiguration> CreateConfigurationForPath(Guid libraryId, string mediaFolderPath) {
        var config = Plugin.Instance.Configuration;
        var mediaFolderConfig = new MediaFolderConfiguration() { LibraryId = libraryId, Path = mediaFolderPath };
        if (File.Exists(Path.Join(mediaFolderPath, ".shoko-ignore"))) {
            mediaFolderConfig.IsIgnored = true;
            return mediaFolderConfig;
        }
        var start = DateTime.UtcNow;
        var attempts = 0;
        var foundLocations = new List<(int, string)>();
        var samplePaths = GetSamplePaths(mediaFolderPath).ToList();
        Logger.LogDebug("Asking remote server if it knows any of the {Count} sampled files in {Path}. (Library={LibraryId})", samplePaths.Count > 100 ? 100 : samplePaths.Count, mediaFolderPath, libraryId);
        foreach (var path in samplePaths) {
            attempts++;
            var partialPath = path[mediaFolderPath.Length..];
            var files = await ApiClient.GetFileByPath(partialPath).ConfigureAwait(false);
            var file = files.Count > 0 ? files[0] : null;
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

        if (mediaFolderConfig.IsMapped) {
            Logger.LogInformation(
                "Found a match for media folder at {Path} in {TimeSpan}. (ManagedFolder={FolderId},RelativePath={RelativePath},MediaLibrary={Path},Attempts={Attempts},Library={LibraryId})",
                mediaFolderPath,
                DateTime.UtcNow - start,
                mediaFolderConfig.ManagedFolderId,
                mediaFolderConfig.ManagedFolderRelativePath,
                mediaFolderPath,
                attempts,
                libraryId
            );
        }
        else {
            Logger.LogWarning(
                "Failed to find a match for media folder at {Path} after {Amount} attempts in {TimeSpan}. (Library={LibraryId})",
                mediaFolderPath,
                attempts,
                DateTime.UtcNow - start,
                libraryId
            );
        }

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

    #region Helpers

    private List<VirtualFolderInfo> GetVirtualFolders()
        => CachedVirtualFolders ??= LibraryManager.GetVirtualFolders();

    #endregion
}
