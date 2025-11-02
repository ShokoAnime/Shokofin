using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Naming.Common;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Configuration;
using Shokofin.Configuration.Models;
using Shokofin.Events;
using Shokofin.Events.Interfaces;
using Shokofin.Events.Stub;
using Shokofin.ExternalIds;
using Shokofin.Resolvers.Models;
using Shokofin.Utils;

namespace Shokofin.Resolvers;

public class ShokoLibraryMonitor : IHostedService {
    private readonly ILogger<ShokoLibraryMonitor> Logger;

    private readonly ShokoApiClient ApiClient;

    private readonly EventDispatchService Events;

    private readonly MediaFolderConfigurationService ConfigurationService;

    private readonly ILibraryManager LibraryManager;

    private readonly ILibraryMonitor LibraryMonitor;

    private readonly LibraryScanWatcher LibraryScanWatcher;

    private readonly NamingOptions NamingOptions;

    private readonly GuardedMemoryCache Cache;

    private readonly ConcurrentDictionary<string, ShokoWatcher> FileSystemWatchers = new();

    /// <summary>
    /// A delay so magical it will give Shoko Server some time to finish it's
    /// rename/move operation before we ask it if it knows the path.
    /// </summary>
    private const int MagicalDelay = 5000; // 5 seconds in milliseconds… for now.

    // follow the core jf behavior, but use config added/removed instead of library added/removed.

    public ShokoLibraryMonitor(
        ILogger<ShokoLibraryMonitor> logger,
        ShokoApiClient apiClient,
        EventDispatchService events,
        MediaFolderConfigurationService configurationService,
        ILibraryManager libraryManager,
        ILibraryMonitor libraryMonitor,
        LibraryScanWatcher libraryScanWatcher,
        NamingOptions namingOptions
    ) {
        Logger = logger;
        ApiClient = apiClient;
        Events = events;
        ConfigurationService = configurationService;
        ConfigurationService.LibraryConfigurationAdded += OnLibraryConfigurationAddedOrChanged;
        ConfigurationService.LibraryConfigurationChanged += OnLibraryConfigurationAddedOrChanged;
        ConfigurationService.LibraryConfigurationRemoved += OnLibraryConfigurationRemoved;
        ConfigurationService.MediaFolderConfigurationAdded += OnMediaFolderConfigurationAdded;
        ConfigurationService.MediaFolderConfigurationRemoved += OnMediaFolderConfigurationRemoved;
        LibraryManager = libraryManager;
        LibraryMonitor = libraryMonitor;
        LibraryScanWatcher = libraryScanWatcher;
        LibraryScanWatcher.ValueChanged += OnLibraryScanRunningChanged;
        NamingOptions = namingOptions;
        Cache = new(logger, new() { ExpirationScanFrequency = TimeSpan.FromSeconds(30) }, new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(1) });
    }

    ~ShokoLibraryMonitor() {
        ConfigurationService.LibraryConfigurationAdded -= OnLibraryConfigurationAddedOrChanged;
        ConfigurationService.LibraryConfigurationChanged -= OnLibraryConfigurationAddedOrChanged;
        ConfigurationService.LibraryConfigurationRemoved -= OnLibraryConfigurationRemoved;
        ConfigurationService.MediaFolderConfigurationAdded -= OnMediaFolderConfigurationAdded;
        ConfigurationService.MediaFolderConfigurationRemoved -= OnMediaFolderConfigurationRemoved;
        LibraryScanWatcher.ValueChanged -= OnLibraryScanRunningChanged;
    }

    Task IHostedService.StartAsync(CancellationToken cancellationToken) {
        StartWatching();
        return Task.CompletedTask;
    }

    Task IHostedService.StopAsync(CancellationToken cancellationToken) {
        StopWatching();
        return Task.CompletedTask;
    }

    public void StartWatching() {
        // add blockers/watchers for every media folder with VFS enabled and real time monitoring enabled.
        foreach (var libraryConfig in Plugin.Instance.Configuration.Libraries.ToList()) {
            if (
                !libraryConfig.IsVirtualFileSystemEnabled ||
                LibraryManager.GetItemById(libraryConfig.Id) is not Folder libraryFolder ||
                LibraryManager.GetLibraryOptions(libraryFolder) is not { } libraryOptions ||
                !libraryOptions.EnableRealtimeMonitor
            )
                continue;

            foreach (var mediaConfig in libraryConfig.MediaFolders) 
                StartWatchingMediaFolder(mediaConfig);
        }
    }

    public void StopWatching() {
        foreach (var path in FileSystemWatchers.Keys.ToList())
            StopWatchingPath(path);
    }

    private void OnLibraryScanRunningChanged(object? sender, bool isScanRunning) {
        if (isScanRunning)
            StopWatching();
        else
            StartWatching();
    }

    private void OnLibraryConfigurationAddedOrChanged(object? sender, LibraryConfigurationChangedEventArgs eventArgs) {
        // Don't add/remove watchers during a scan.
        if (LibraryScanWatcher.IsScanRunning)
            return;

        if (
            eventArgs.LibraryConfiguration.IsVirtualFileSystemEnabled &&
            LibraryManager.GetItemById(eventArgs.LibraryConfiguration.Id) is Folder libraryFolder &&
            LibraryManager.GetLibraryOptions(libraryFolder) is { } libraryOptions &&
            libraryOptions.EnableRealtimeMonitor
        ) {
            foreach (var mediaConfig in eventArgs.MediaFolderConfigurations)
                StartWatchingMediaFolder(mediaConfig);
        }
        else {
            foreach (var mediaConfig in eventArgs.MediaFolderConfigurations)
                StopWatchingPath(mediaConfig.Path);
        }
    }

    private void OnLibraryConfigurationRemoved(object? sender, LibraryConfigurationChangedEventArgs eventArgs) {
        // Don't add/remove watchers during a scan.
        if (LibraryScanWatcher.IsScanRunning)
            return;

        foreach (var mediaConfig in eventArgs.MediaFolderConfigurations)
            StopWatchingPath(mediaConfig.Path);
    }

    private void OnMediaFolderConfigurationAdded(object? sender, MediaConfigurationChangedEventArgs eventArgs) {
        // Don't add/remove watchers during a scan.
        if (LibraryScanWatcher.IsScanRunning)
            return;

        if (
            eventArgs.LibraryConfiguration.IsVirtualFileSystemEnabled &&
            LibraryManager.GetItemById(eventArgs.LibraryConfiguration.Id) is Folder libraryFolder &&
            LibraryManager.GetLibraryOptions(libraryFolder) is { } libraryOptions &&
            libraryOptions.EnableRealtimeMonitor
        )
            StartWatchingMediaFolder(eventArgs.MediaFolderConfiguration);
        else
            StopWatchingPath(eventArgs.MediaFolderConfiguration.Path);
    }

    private void OnMediaFolderConfigurationRemoved(object? sender, MediaConfigurationChangedEventArgs eventArgs) {
        // Don't add/remove watchers during a scan.
        if (LibraryScanWatcher.IsScanRunning)
            return;

        StopWatchingPath(eventArgs.MediaFolderConfiguration.Path);
    }

    private void StartWatchingMediaFolder(MediaFolderConfiguration config) {
        // Creating a FileSystemWatcher over the LAN can take hundreds of milliseconds, so wrap it in a Task to do it in parallel.
        Task.Run(() => {
            try {
                var watcher = new FileSystemWatcher(config.Path, "*") {
                    IncludeSubdirectories = true,
                    InternalBufferSize = 65536,
                    NotifyFilter = NotifyFilters.CreationTime |
                                    NotifyFilters.DirectoryName |
                                    NotifyFilters.FileName |
                                    NotifyFilters.LastWrite |
                                    NotifyFilters.Size |
                                    NotifyFilters.Attributes
                };

                watcher.Created += OnWatcherChanged;
                watcher.Deleted += OnWatcherChanged;
                watcher.Renamed += OnWatcherChanged;
                watcher.Changed += OnWatcherChanged;
                watcher.Error += OnWatcherError;

                var lease = Events.RegisterEventSubmitter();
                if (FileSystemWatchers.TryAdd(config.Path, new(config, watcher, lease))) {
                    LibraryMonitor.ReportFileSystemChangeBeginning(config.Path);
                    watcher.EnableRaisingEvents = true;
                    Logger.LogInformation("Watching directory {Path}", config.Path);
                }
                else {
                    lease.Dispose();
                    DisposeWatcher(watcher, false);
                }
            }
            catch (Exception ex) {
                Logger.LogError(ex, "Error watching path: {Path}", config.Path);
            }
        });
    }

    private void StopWatchingPath(string path) {
        if (FileSystemWatchers.TryGetValue(path, out var watcher)) {
            DisposeWatcher(watcher.Watcher, true);
        }
    }

    private void DisposeWatcher(FileSystemWatcher watcher, bool removeFromList = true) {
        try {
            using (watcher) {
                Logger.LogInformation("Stopping directory watching for path {Path}", watcher.Path);

                watcher.Created -= OnWatcherChanged;
                watcher.Deleted -= OnWatcherChanged;
                watcher.Renamed -= OnWatcherChanged;
                watcher.Changed -= OnWatcherChanged;
                watcher.Error -= OnWatcherError;

                watcher.EnableRaisingEvents = false;
            }
        }
        finally {
            if (removeFromList && FileSystemWatchers.TryRemove(watcher.Path, out var shokoWatcher)) {
                LibraryMonitor.ReportFileSystemChangeComplete(watcher.Path, false);
                shokoWatcher.SubmitterLease.Dispose();
            }
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs eventArgs) {
        var ex = eventArgs.GetException();
        if (sender is not FileSystemWatcher watcher)
            return;

        Logger.LogError(ex, "Error in Directory watcher for: {Path}", watcher.Path);

        DisposeWatcher(watcher);
    }

    private void OnWatcherChanged(object? sender, FileSystemEventArgs e) {
        try {
            if (sender is not FileSystemWatcher watcher || !FileSystemWatchers.TryGetValue(watcher.Path, out var shokoWatcher))
                return;
            Task.Run(() => ReportFileSystemChanged(shokoWatcher.Configuration, e.ChangeType, e.FullPath));
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Exception in ReportFileSystemChanged. Path: {FullPath}", e.FullPath);
        }
    }

    public async Task ReportFileSystemChanged(MediaFolderConfiguration mediaConfig, WatcherChangeTypes changeTypes, string path) {
        Logger.LogTrace("Found potential path with change {ChangeTypes}; {Path}", changeTypes, path);

        if (!path.StartsWith(mediaConfig.Path)) {
            Logger.LogTrace("Skipped path because it is not in the watched folder; {Path}", path);
            return;
        }

        if (!IsVideoFile(path)) {
            Logger.LogTrace("Skipped path because it is not a video file; {Path}", path);
            return;
        }

        await Task.Delay(MagicalDelay).ConfigureAwait(false);

        if (changeTypes is not WatcherChangeTypes.Deleted && !File.Exists(path)) {
            Logger.LogTrace("Skipped path because it is disappeared after awhile before we could process it; {Path}", path);
            return;
        }

        // Using a "cache" here is more to ensure we only run for the same path once in a given time span.
        await Cache.GetOrCreateAsync(
            path,
            (_) => Logger.LogTrace("Skipped path because it was handled within a second ago; {Path}", path),
            async () => {
                string? fileId = null;
                FileEventArgsStub eventArgs;
                var reason = changeTypes is WatcherChangeTypes.Deleted ? (
                    UpdateReason.MetadataRemoved
                ) : changeTypes is WatcherChangeTypes.Created ? (
                    UpdateReason.MetadataAdded
                ) : (
                    UpdateReason.MetadataUpdated
                );
                var relativePath = path[mediaConfig.Path.Length..];
                using (Plugin.Instance.Tracker.Enter($"Library Monitor: Path=\"{path}\"")) {
                    var files = await ApiClient.GetFileByPath(relativePath).ConfigureAwait(false);
                    var file0 = files.FirstOrDefault(file => file.Locations.Any(location => location.ManagedFolderId == mediaConfig.ManagedFolderId && location.RelativePath == mediaConfig.ManagedFolderRelativePath + relativePath));
                    if (file0 is not null) {
                        var fileLocation = file0.Locations.First(location => location.ManagedFolderId == mediaConfig.ManagedFolderId && location.RelativePath == mediaConfig.ManagedFolderRelativePath + relativePath);
                        eventArgs = new FileEventArgsStub(fileLocation, file0);
                    }
                    else if (reason is not UpdateReason.MetadataRemoved) {
                        Logger.LogTrace("Skipped path because it is not a shoko managed file; {Path}", path);
                        return null;
                    }
                    else if (LibraryManager.FindByPath(path, false) is not Video video) {
                        Logger.LogTrace("Skipped path because it is not a shoko managed file; {Path}", path);
                        return null;
                    }
                    else if (!video.TryGetProviderId(ProviderNames.ShokoFile, out fileId)) {
                        Logger.LogTrace("Skipped path because it is not a shoko managed file; {Path}", path);
                        return null;
                    }
                    else if (await ApiClient.GetFile(fileId).ConfigureAwait(false) is { } file1) {
                        var fileLocation = file1.Locations.First(location => location.ManagedFolderId == mediaConfig.ManagedFolderId && location.RelativePath == mediaConfig.ManagedFolderRelativePath + relativePath);
                        eventArgs = new FileEventArgsStub(fileLocation, file1);
                    }
                    else {
                        Logger.LogTrace("Failed to get file info from Shoko during a file deleted event. (File={FileId})", fileId);
                        eventArgs = new FileEventArgsStub(int.Parse(fileId), null, mediaConfig.ManagedFolderId, relativePath, []);
                    }
                }

                Logger.LogDebug(
                    "File {EventName}; {ManagedFolderId} {Path} (File={FileId},Location={LocationId},CrossReferences={HasCrossReferences})",
                    reason,
                    eventArgs.ManagedFolderId,
                    relativePath,
                    eventArgs.FileId,
                    eventArgs.FileLocationId,
                    true
                );

                if (LibraryScanWatcher.IsScanRunning) {
                    Logger.LogTrace(
                        "Library scan is running. Skipping emit of file event. (File={FileId},Location={LocationId})",
                        eventArgs.FileId,
                        eventArgs.FileLocationId
                    );
                    return null;
                }

                Events.AddFileEvent(eventArgs.FileId, reason, eventArgs.ManagedFolderId, relativePath, eventArgs);
                return eventArgs;
            }
        ).ConfigureAwait(false);
    }

    private bool IsVideoFile(string path)
        => NamingOptions.VideoFileExtensions.Contains(Path.GetExtension(path));
}
