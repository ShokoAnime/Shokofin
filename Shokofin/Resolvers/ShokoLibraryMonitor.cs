using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Emby.Naming.Common;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Configuration;
using Shokofin.SignalR.Interfaces;
using Shokofin.SignalR.Models;
using Shokofin.Utils;

namespace Shokofin.Resolvers;

public class ShokoLibraryMonitor : IServerEntryPoint, IDisposable
{
    private readonly ILogger<ShokoLibraryMonitor> Logger;

    private readonly ShokoAPIClient ApiClient;

    private readonly ShokoResolveManager ResolveManager;

    private readonly ILibraryManager LibraryManager;

    private readonly ILibraryMonitor LibraryMonitor;

    private readonly LibraryScanWatcher LibraryScanWatcher;

    private readonly NamingOptions NamingOptions;

    private readonly ConcurrentDictionary<string, ShokoWatcher> FileSystemWatchers = new();

    // follow the core jf behavior, but use config added/removed instead of library added/removed.

    public ShokoLibraryMonitor(
        ILogger<ShokoLibraryMonitor> logger,
        ShokoAPIClient apiClient,
        ShokoResolveManager resolveManager,
        ILibraryManager libraryManager,
        ILibraryMonitor libraryMonitor,
        LibraryScanWatcher libraryScanWatcher,
        NamingOptions namingOptions
    )
    {
        Logger = logger;
        ApiClient = apiClient;
        ResolveManager = resolveManager;
        ResolveManager.ConfigurationAdded += OnMediaFolderConfigurationAddedOrUpdated;
        ResolveManager.ConfigurationUpdated += OnMediaFolderConfigurationAddedOrUpdated;
        ResolveManager.ConfigurationRemoved += OnMediaFolderConfigurationRemoved;
        LibraryManager = libraryManager;
        LibraryMonitor = libraryMonitor;
        LibraryScanWatcher = libraryScanWatcher;
        LibraryScanWatcher.ValueChanged += OnLibraryScanRunningChanged;
        NamingOptions = namingOptions;
    }

    ~ShokoLibraryMonitor()
    {
        ResolveManager.ConfigurationAdded -= OnMediaFolderConfigurationAddedOrUpdated;
        ResolveManager.ConfigurationUpdated  -= OnMediaFolderConfigurationAddedOrUpdated;
        ResolveManager.ConfigurationRemoved -= OnMediaFolderConfigurationRemoved;
        LibraryScanWatcher.ValueChanged -= OnLibraryScanRunningChanged;
    }

    Task IServerEntryPoint.RunAsync()
    {
        StartWatching();
        return Task.CompletedTask;
    }

    void IDisposable.Dispose()
    {
        GC.SuppressFinalize(this);
        StopWatching();
    }

    public void StartWatching()
    {
        // add blockers/watchers for every media folder with VFS enabled and real time monitoring enabled.
        foreach (var mediaConfig in Plugin.Instance.Configuration.MediaFolders) {
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

                var lease = ResolveManager.RegisterEventSubmitter();
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
        if (!path.StartsWith(mediaConfig.MediaFolderPath)) {
            Logger.LogTrace("Skipped path because it is not in the watched folder; {Path}", path);
            return;
        }

        if (!IsVideoFile(path)) {
            Logger.LogTrace("Skipped path because it is not a video file; {Path}", path);
            return;
        }

        var relativePath = path[mediaConfig.MediaFolderPath.Length..];
        var files = await ApiClient.GetFileByPath(relativePath);
        var file = files.FirstOrDefault(file => file.Locations.Any(location => location.ImportFolderId == mediaConfig.ImportFolderId && location.RelativePath == relativePath));
        if (file is null) {
            Logger.LogTrace("Skipped file because it is not a shoko managed file; {Path}", path);
            return;
        }

        var reason = changeTypes == WatcherChangeTypes.Deleted ? UpdateReason.Removed : changeTypes == WatcherChangeTypes.Created ? UpdateReason.Added : UpdateReason.Updated;
        var fileLocation = file.Locations.First(location => location.ImportFolderId == mediaConfig.ImportFolderId && location.RelativePath == relativePath);
        Logger.LogDebug(
            "File {EventName}; {ImportFolderId} {Path} (File={FileId},Location={LocationId},CrossReferences={HasCrossReferences})",
            reason,
            fileLocation.ImportFolderId,
            relativePath,
            file.Id,
            fileLocation.Id,
            true
        );

        if (LibraryScanWatcher.IsScanRunning) {
            Logger.LogTrace(
                "Library scan is running. Skipping emit of file event. (File={FileId},Location={LocationId})",
                file.Id,
                fileLocation.Id
            );
            return;
        }

        ResolveManager.AddFileEvent(file.Id, reason, fileLocation.ImportFolderId, relativePath, new FileEventArgsStub(fileLocation, file));
    }
    private bool IsVideoFile(string path)
        => NamingOptions.VideoFileExtensions.Contains(Path.GetExtension(path));

}
