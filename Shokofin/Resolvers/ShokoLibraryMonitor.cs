using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Naming.Common;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
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

using ApiException = Shokofin.API.Models.ApiException;

namespace Shokofin.Resolvers;

public class ShokoLibraryMonitor : IHostedService
{
    private readonly ILogger<ShokoLibraryMonitor> Logger;

    private readonly ShokoAPIClient ApiClient;

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
        ShokoAPIClient apiClient,
        EventDispatchService events,
        MediaFolderConfigurationService configurationService,
        ILibraryManager libraryManager,
        ILibraryMonitor libraryMonitor,
        LibraryScanWatcher libraryScanWatcher,
        NamingOptions namingOptions
    )
    {
        Logger = logger;
        ApiClient = apiClient;
        Events = events;
        ConfigurationService = configurationService;
        ConfigurationService.ConfigurationAdded += OnMediaFolderConfigurationAddedOrUpdated;
        ConfigurationService.ConfigurationUpdated += OnMediaFolderConfigurationAddedOrUpdated;
        ConfigurationService.ConfigurationRemoved += OnMediaFolderConfigurationRemoved;
        LibraryManager = libraryManager;
        LibraryMonitor = libraryMonitor;
        LibraryScanWatcher = libraryScanWatcher;
        LibraryScanWatcher.ValueChanged += OnLibraryScanRunningChanged;
        NamingOptions = namingOptions;
        Cache = new(logger, new() { ExpirationScanFrequency = TimeSpan.FromSeconds(30) }, new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(1) });
    }

    ~ShokoLibraryMonitor()
    {
        ConfigurationService.ConfigurationAdded -= OnMediaFolderConfigurationAddedOrUpdated;
        ConfigurationService.ConfigurationUpdated  -= OnMediaFolderConfigurationAddedOrUpdated;
        ConfigurationService.ConfigurationRemoved -= OnMediaFolderConfigurationRemoved;
        LibraryScanWatcher.ValueChanged -= OnLibraryScanRunningChanged;
    }

    Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        StartWatching();
        return Task.CompletedTask;
    }

    Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        StopWatching();
        return Task.CompletedTask;
    }

    public void StartWatching()
    {
        // add blockers/watchers for every media folder with VFS enabled and real time monitoring enabled.
        foreach (var mediaConfig in Plugin.Instance.Configuration.MediaFolders.ToList()) {
            if (LibraryManager.GetItemById(mediaConfig.MediaFolderId) is not Folder mediaFolder)
                continue;

            var libraryOptions = LibraryManager.GetLibraryOptions(mediaFolder);
            if (libraryOptions != null && libraryOptions.EnableRealtimeMonitor && mediaConfig.IsVirtualFileSystemEnabled)
                StartWatchingMediaFolder(mediaFolder, mediaConfig);
        }
    }

    public void StopWatching()
    {
        foreach (var path in FileSystemWatchers.Keys.ToList())
            StopWatchingPath(path);
    }

    private void OnLibraryScanRunningChanged(object? sender, bool isScanRunning)
    {
        if (isScanRunning)
            StopWatching();
        else
            StartWatching();
    }

    private void OnMediaFolderConfigurationAddedOrUpdated(object? sender, MediaConfigurationChangedEventArgs eventArgs)
    {
        // Don't add/remove watchers during a scan.
        if (LibraryScanWatcher.IsScanRunning)
            return;

        var libraryOptions = LibraryManager.GetLibraryOptions(eventArgs.MediaFolder);
        if (libraryOptions != null && libraryOptions.EnableRealtimeMonitor && eventArgs.Configuration.IsVirtualFileSystemEnabled)
            StartWatchingMediaFolder(eventArgs.MediaFolder, eventArgs.Configuration);
        else
            StopWatchingPath(eventArgs.MediaFolder.Path);
    }

    private void OnMediaFolderConfigurationRemoved(object? sender, MediaConfigurationChangedEventArgs eventArgs)
    {
        // Don't add/remove watchers during a scan.
        if (LibraryScanWatcher.IsScanRunning)
            return;

        StopWatchingPath(eventArgs.MediaFolder.Path);
    }

    private void StartWatchingMediaFolder(Folder mediaFolder, MediaFolderConfiguration config)
    {
        // Creating a FileSystemWatcher over the LAN can take hundreds of milliseconds, so wrap it in a Task to do it in parallel.
        Task.Run(() => {
            try {
                var watcher = new FileSystemWatcher(mediaFolder.Path, "*") {
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
                if (FileSystemWatchers.TryAdd(mediaFolder.Path, new(mediaFolder, config, watcher, lease))) {
                    LibraryMonitor.ReportFileSystemChangeBeginning(mediaFolder.Path);
                    watcher.EnableRaisingEvents = true;
                    Logger.LogInformation("Watching directory {Path}", mediaFolder.Path);
                }
                else {
                    lease.Dispose();
                    DisposeWatcher(watcher, false);
                }
            }
            catch (Exception ex) {
                Logger.LogError(ex, "Error watching path: {Path}", mediaFolder.Path);
            }
        });
    }

    private void StopWatchingPath(string path)
    {
        if (FileSystemWatchers.TryGetValue(path, out var watcher))
        {
            DisposeWatcher(watcher.Watcher, true);
        }
    }

    private void DisposeWatcher(FileSystemWatcher watcher, bool removeFromList = true)
    {
        try
        {
            using (watcher)
            {
                Logger.LogInformation("Stopping directory watching for path {Path}", watcher.Path);

                watcher.Created -= OnWatcherChanged;
                watcher.Deleted -= OnWatcherChanged;
                watcher.Renamed -= OnWatcherChanged;
                watcher.Changed -= OnWatcherChanged;
                watcher.Error -= OnWatcherError;

                watcher.EnableRaisingEvents = false;
            }
        }
        finally
        {
            if (removeFromList && FileSystemWatchers.TryRemove(watcher.Path, out var shokoWatcher)) {
                LibraryMonitor.ReportFileSystemChangeComplete(watcher.Path, false);
                shokoWatcher.SubmitterLease.Dispose();
            }
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs eventArgs)
    {
        var ex = eventArgs.GetException();
        if (sender is not FileSystemWatcher watcher)
            return;

        Logger.LogError(ex, "Error in Directory watcher for: {Path}", watcher.Path);

        DisposeWatcher(watcher);
    }

    private void OnWatcherChanged(object? sender, FileSystemEventArgs e)
    {
        try
        {
            if (sender is not FileSystemWatcher watcher || !FileSystemWatchers.TryGetValue(watcher.Path, out var shokoWatcher))
                return;
            Task.Run(() => ReportFileSystemChanged(shokoWatcher.Configuration, e.ChangeType, e.FullPath));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception in ReportFileSystemChanged. Path: {FullPath}", e.FullPath);
        }
    }

    public async Task ReportFileSystemChanged(MediaFolderConfiguration mediaConfig, WatcherChangeTypes changeTypes, string path)
    {
        Logger.LogTrace("Found potential path with change {ChangeTypes}; {Path}", changeTypes, path);

        if (!path.StartsWith(mediaConfig.MediaFolderPath)) {
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
                IFileEventArgs eventArgs;
                var reason = changeTypes is WatcherChangeTypes.Deleted ? UpdateReason.Removed : changeTypes is WatcherChangeTypes.Created ? UpdateReason.Added : UpdateReason.Updated;
                var relativePath = path[mediaConfig.MediaFolderPath.Length..];
                var trackerId = Plugin.Instance.Tracker.Add($"Library Monitor: Path=\"{path}\"");
                try {
                    var files = await ApiClient.GetFileByPath(relativePath);
                    var file = files.FirstOrDefault(file => file.Locations.Any(location => location.ImportFolderId == mediaConfig.ImportFolderId && location.RelativePath == relativePath));
                    if (file is null) {
                        if (reason is not UpdateReason.Removed) {
                            Logger.LogTrace("Skipped path because it is not a shoko managed file; {Path}", path);
                            return null;
                        }
                        if (LibraryManager.FindByPath(path, false) is not Video video) {
                            Logger.LogTrace("Skipped path because it is not a shoko managed file; {Path}", path);
                            return null;
                        }
                        if (!video.ProviderIds.TryGetValue(ShokoFileId.Name, out fileId)) {
                            Logger.LogTrace("Skipped path because it is not a shoko managed file; {Path}", path);
                            return null;
                        }
                        // It may throw an ApiException with 404 here,
                        file = await ApiClient.GetFile(fileId);
                    }

                    var fileLocation = file.Locations.First(location => location.ImportFolderId == mediaConfig.ImportFolderId && location.RelativePath == relativePath);
                    eventArgs = new FileEventArgsStub(fileLocation, file);
                }
                // which we catch here.
                catch (ApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound) {
                    if (fileId is null) {
                        Logger.LogTrace("Skipped path because it is not a shoko managed file; {Path}", path);
                        return null;
                    }

                    Logger.LogTrace("Failed to get file info from Shoko during a file deleted event. (File={FileId})", fileId);
                    eventArgs = new FileEventArgsStub(int.Parse(fileId), null, mediaConfig.ImportFolderId, relativePath, Array.Empty<IFileEventArgs.FileCrossReference>());
                }
                finally {
                    Plugin.Instance.Tracker.Remove(trackerId);
                }

                Logger.LogDebug(
                    "File {EventName}; {ImportFolderId} {Path} (File={FileId},Location={LocationId},CrossReferences={HasCrossReferences})",
                    reason,
                    eventArgs.ImportFolderId,
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

                Events.AddFileEvent(eventArgs.FileId, reason, eventArgs.ImportFolderId, relativePath, eventArgs);
                return eventArgs;
            }
        );
    }

    private bool IsVideoFile(string path)
        => NamingOptions.VideoFileExtensions.Contains(Path.GetExtension(path));
}
