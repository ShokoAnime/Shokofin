using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Info;
using Shokofin.API.Models;
using Shokofin.Configuration;
using Shokofin.Events.Interfaces;
using Shokofin.ExternalIds;
using Shokofin.Resolvers;
using Shokofin.Resolvers.Models;
using Shokofin.Utils;

using File = System.IO.File;
using IDirectoryService = MediaBrowser.Controller.Providers.IDirectoryService;
using ImageType = MediaBrowser.Model.Entities.ImageType;
using LibraryOptions = MediaBrowser.Model.Configuration.LibraryOptions;
using MetadataRefreshMode = MediaBrowser.Controller.Providers.MetadataRefreshMode;
using Timer = System.Timers.Timer;

namespace Shokofin.Events;

public class EventDispatchService
{
    private readonly ShokoAPIManager ApiManager;

    private readonly ShokoAPIClient ApiClient;

    private readonly ILibraryManager LibraryManager;

    private readonly ILibraryMonitor LibraryMonitor;

    private readonly LibraryScanWatcher LibraryScanWatcher;

    private readonly MediaFolderConfigurationService ConfigurationService;

    private readonly VirtualFileSystemService ResolveManager;

    private readonly IFileSystem FileSystem;

    private readonly IDirectoryService DirectoryService;

    private readonly ILogger<EventDispatchService> Logger;

    private int ChangesDetectionSubmitterCount = 0;

    private readonly Timer ChangesDetectionTimer;

    private readonly Dictionary<string, (DateTime LastUpdated, List<IMetadataUpdatedEventArgs> List, Guid trackerId)> ChangesPerSeries = [];

    private readonly Dictionary<int, (DateTime LastUpdated, List<(UpdateReason Reason, int ImportFolderId, string Path, IFileEventArgs Event)> List, Guid trackerId)> ChangesPerFile = [];

    private readonly Dictionary<string, (int refCount, DateTime delayEnd)> MediaFolderChangeMonitor = [];

    // It's so magical that it matches the magical value in the library monitor in JF core. 🪄
    private const int MagicalDelayValue = 45000;

    private static readonly TimeSpan DetectChangesThreshold = TimeSpan.FromSeconds(5);

    public EventDispatchService(
        ShokoAPIManager apiManager,
        ShokoAPIClient apiClient,
        ILibraryManager libraryManager,
        VirtualFileSystemService resolveManager,
        MediaFolderConfigurationService configurationService,
        ILibraryMonitor libraryMonitor,
        LibraryScanWatcher libraryScanWatcher,
        IFileSystem fileSystem,
        IDirectoryService directoryService,
        ILogger<EventDispatchService> logger
    )
    {
        ApiManager = apiManager;
        ApiClient = apiClient;
        LibraryManager = libraryManager;
        LibraryMonitor = libraryMonitor;
        ResolveManager = resolveManager;
        ConfigurationService = configurationService;
        LibraryScanWatcher = libraryScanWatcher;
        FileSystem = fileSystem;
        DirectoryService = directoryService;
        Logger = logger;
        ChangesDetectionTimer = new() { AutoReset = true, Interval = TimeSpan.FromSeconds(4).TotalMilliseconds };
        ChangesDetectionTimer.Elapsed += OnIntervalElapsed;
    }

    ~EventDispatchService()
    {
        
        ChangesDetectionTimer.Elapsed -= OnIntervalElapsed;
    }

    #region Event Detection

    public IDisposable RegisterEventSubmitter()
    {
        var count = ChangesDetectionSubmitterCount++;
        if (count is 0)
            ChangesDetectionTimer.Start();

        return new DisposableAction(() => DeregisterEventSubmitter());
    }

    private void DeregisterEventSubmitter()
    {
        var count = --ChangesDetectionSubmitterCount;
        if (count is 0) {
            ChangesDetectionTimer.Stop();
            if (ChangesPerFile.Count > 0)
                ClearFileEvents();
            if (ChangesPerSeries.Count > 0)
                ClearMetadataUpdatedEvents();
        }
    }

    private void OnIntervalElapsed(object? sender, ElapsedEventArgs eventArgs)
    {
        var filesToProcess = new List<(int, List<(UpdateReason Reason, int ImportFolderId, string Path, IFileEventArgs Event)>, Guid trackerId)>();
        var seriesToProcess = new List<(string, List<IMetadataUpdatedEventArgs>, Guid trackerId)>();
        lock (ChangesPerFile) {
            if (ChangesPerFile.Count > 0) {
                var now = DateTime.Now;
                foreach (var (fileId, (lastUpdated, list, trackerId)) in ChangesPerFile) {
                    if (now - lastUpdated < DetectChangesThreshold)
                        continue;
                    filesToProcess.Add((fileId, list, trackerId));
                }
                foreach (var (fileId, _, _) in filesToProcess)
                    ChangesPerFile.Remove(fileId);
            }
        }
        lock (ChangesPerSeries) {
            if (ChangesPerSeries.Count > 0) {
                var now = DateTime.Now;
                foreach (var (metadataId, (lastUpdated, list, trackerId)) in ChangesPerSeries) {
                    if (now - lastUpdated < DetectChangesThreshold)
                        continue;
                    seriesToProcess.Add((metadataId, list, trackerId));
                }
                foreach (var (metadataId, _, _) in seriesToProcess)
                    ChangesPerSeries.Remove(metadataId);
            }
        }
        foreach (var (fileId, changes, trackerId) in filesToProcess)
            Task.Run(() => ProcessFileEvents(fileId, changes, trackerId));
        foreach (var (metadataId, changes, trackerId) in seriesToProcess)
            Task.Run(() => ProcessMetadataEvents(metadataId, changes, trackerId));
    }

    private void ClearFileEvents()
    {
        var filesToProcess = new List<(int, List<(UpdateReason Reason, int ImportFolderId, string Path, IFileEventArgs Event)>, Guid trackerId)>();
        lock (ChangesPerFile) {
            foreach (var (fileId, (lastUpdated, list, trackerId)) in ChangesPerFile) {
                filesToProcess.Add((fileId, list, trackerId));
            }
            ChangesPerFile.Clear();
        }
        foreach (var (fileId, changes, trackerId) in filesToProcess)
            Task.Run(() => ProcessFileEvents(fileId, changes, trackerId));
    }

    private void ClearMetadataUpdatedEvents()
    {
        var seriesToProcess = new List<(string, List<IMetadataUpdatedEventArgs>, Guid trackerId)>();
        lock (ChangesPerSeries) {
            foreach (var (metadataId, (lastUpdated, list, trackerId)) in ChangesPerSeries) {
                seriesToProcess.Add((metadataId, list, trackerId));
            }
            ChangesPerSeries.Clear();
        }
        foreach (var (metadataId, changes, trackerId) in seriesToProcess)
            Task.Run(() => ProcessMetadataEvents(metadataId, changes, trackerId));
    }

    #endregion

    #region File Events

    public void AddFileEvent(int fileId, UpdateReason reason, int importFolderId, string filePath, IFileEventArgs eventArgs)
    {
        lock (ChangesPerFile) {
            if (ChangesPerFile.TryGetValue(fileId, out var tuple))
                tuple.LastUpdated = DateTime.Now;
            else
                ChangesPerFile.Add(fileId, tuple = (DateTime.Now, [], Plugin.Instance.Tracker.Add($"File event. (Reason=\"{reason}\",ImportFolder={eventArgs.ImportFolderId},RelativePath=\"{eventArgs.RelativePath}\")")));
            tuple.List.Add((reason, importFolderId, filePath, eventArgs));
        }
    }

    private async Task ProcessFileEvents(int fileId, List<(UpdateReason Reason, int ImportFolderId, string Path, IFileEventArgs Event)> changes, Guid trackerId)
    {
        try {
            if (LibraryScanWatcher.IsScanRunning) {
                Logger.LogInformation("Skipped processing {EventCount} file change events because a library scan is running. (File={FileId})", changes.Count, fileId);
                return;
            }

            Logger.LogInformation("Processing {EventCount} file change events… (File={FileId})", changes.Count, fileId);

            // Something was added or updated.
            var locationsToNotify = new List<string>();
            var mediaFoldersToNotify = new Dictionary<string, (string pathToReport, Folder mediaFolder)>();
            var seriesIds = await GetSeriesIdsForFile(fileId, changes.Select(t => t.Event).LastOrDefault(e => e.HasCrossReferences));
            var libraries = ConfigurationService.GetAvailableMediaFoldersForLibrariesForEvents(c => c.IsFileEventsEnabled);
            var (reason, importFolderId, relativePath, lastEvent) = changes.Last();
            if (reason is not UpdateReason.Removed) {
                Logger.LogTrace("Processing file changed. (File={FileId})", fileId);
                foreach (var (vfsPath, mainMediaFolderPath, collectionType, mediaConfigs) in libraries) {
                    foreach (var (importFolderSubPath, vfsEnabled, mediaFolderPaths) in mediaConfigs.ToImportFolderList(importFolderId, relativePath)) {
                        foreach (var mediaFolderPath in mediaFolderPaths) {
                            var sourceLocation = Path.Join(mediaFolderPath, relativePath[importFolderSubPath.Length..]);
                            if (!File.Exists(sourceLocation))
                                continue;

                            // Let the core logic handle the rest.
                            if (!vfsEnabled) {
                                locationsToNotify.Add(sourceLocation);
                                break;
                            }

                            var result = new LinkGenerationResult();
                            var topFolders = new HashSet<string>();
                            var vfsLocations = (await Task.WhenAll(seriesIds.Select(seriesId => ResolveManager.GenerateLocationsForFile(collectionType, vfsPath, sourceLocation, fileId.ToString(), seriesId))).ConfigureAwait(false))
                                .Where(tuple => tuple.symbolicLinks.Length > 0 && tuple.importedAt.HasValue)
                                .ToList();
                            foreach (var (symLinks, importDate) in vfsLocations) {
                                result += ResolveManager.GenerateSymbolicLinks(sourceLocation, symLinks, importDate!.Value);
                                foreach (var path in symLinks.Select(path => Path.Join(vfsPath, path[(vfsPath.Length + 1)..].Split(Path.DirectorySeparatorChar).First())).Distinct())
                                    topFolders.Add(path);
                            }

                            // Remove old links for file.
                            var videos = LibraryManager
                                .GetItemList(
                                    new() {
                                        AncestorIds = mediaConfigs.Select(c => c.MediaFolderId).ToArray(),
                                        HasAnyProviderId = new Dictionary<string, string> { { ShokoFileId.Name, fileId.ToString() } },
                                        DtoOptions = new(true),
                                    },
                                    true
                                );
                            Logger.LogTrace("Found {Count} potential videos to remove", videos.Count);
                            foreach (var video in videos) {
                                if (string.IsNullOrEmpty(video.Path) || !video.Path.StartsWith(vfsPath) || result.Paths.Contains(video.Path)) {
                                    Logger.LogTrace("Skipped a {Kind} to remove with path {Path}", video.GetBaseItemKind(), video.Path);
                                    continue;
                                }
                                Logger.LogTrace("Found a {Kind} to remove with path {Path}", video.GetBaseItemKind(), video.Path);
                                RemoveSymbolicLink(video.Path);
                                topFolders.Add(Path.Join(vfsPath, video.Path[(vfsPath.Length + 1)..].Split(Path.DirectorySeparatorChar).First()));
                                locationsToNotify.Add(video.Path);
                                result.RemovedVideos++;
                            }

                            result.Print(Logger, mediaFolderPath);

                            // If all the "top-level-folders" exist, then let the core logic handle the rest.
                            if (topFolders.All(path => LibraryManager.FindByPath(path, true) is not null)) {
                                locationsToNotify.AddRange(vfsLocations.SelectMany(tuple => tuple.symbolicLinks));
                            }
                            // Else give the core logic _any_ file or folder placed directly in the media folder, so it will schedule the media folder to be refreshed.
                            else {
                                var fileOrFolder = FileSystem.GetFileSystemEntryPaths(mainMediaFolderPath, false).FirstOrDefault();
                                if (!string.IsNullOrEmpty(fileOrFolder))
                                    mediaFoldersToNotify.TryAdd(mainMediaFolderPath, (fileOrFolder, mainMediaFolderPath.GetFolderForPath()));
                            }
                            break;
                        }
                    }
                }
            }
            // Something was removed, so assume the location is gone.
            else if (changes.FirstOrDefault(t => t.Reason is UpdateReason.Removed).Event is IFileEventArgs firstRemovedEvent) {
                Logger.LogTrace("Processing file removed. (File={FileId})", fileId);
                relativePath = firstRemovedEvent.RelativePath;
                importFolderId = firstRemovedEvent.ImportFolderId;
                foreach (var (vfsPath, mainMediaFolderPath, collectionType, mediaConfigs) in libraries) {
                    foreach (var (importFolderSubPath, vfsEnabled, mediaFolderPaths) in mediaConfigs.ToImportFolderList(importFolderId, relativePath)) {
                        foreach (var mediaFolderPath in mediaFolderPaths) {
                            // Let the core logic handle the rest.
                            if (!vfsEnabled) {
                                var sourceLocation = Path.Join(mediaFolderPath, relativePath[importFolderSubPath.Length..]);
                                locationsToNotify.Add(sourceLocation);
                                break;
                            }

                            // Check if we can use another location for the file.
                            var result = new LinkGenerationResult();
                            var vfsSymbolicLinks = new HashSet<string>();
                            var topFolders = new HashSet<string>();
                            var newSourceLocation = await GetNewSourceLocation(importFolderId, importFolderSubPath, fileId, relativePath, mediaFolderPath);
                            if (!string.IsNullOrEmpty(newSourceLocation)) {
                                var vfsLocations = (await Task.WhenAll(seriesIds.Select(seriesId => ResolveManager.GenerateLocationsForFile(collectionType, vfsPath, newSourceLocation, fileId.ToString(), seriesId))).ConfigureAwait(false))
                                .Where(tuple => tuple.symbolicLinks.Length > 0 && tuple.importedAt.HasValue)
                                    .ToList();
                                foreach (var (symLinks, importDate) in vfsLocations) {
                                    result += ResolveManager.GenerateSymbolicLinks(newSourceLocation, symLinks, importDate!.Value);
                                    foreach (var path in symLinks.Select(path => Path.Join(vfsPath, path[(vfsPath.Length + 1)..].Split(Path.DirectorySeparatorChar).First())).Distinct())
                                        topFolders.Add(path);
                                }
                                vfsSymbolicLinks = vfsLocations.SelectMany(tuple => tuple.symbolicLinks).ToHashSet();
                            }

                            // Remove old links for file.
                            var videos = LibraryManager
                                .GetItemList(
                                    new() {
                                        HasAnyProviderId = new Dictionary<string, string> { { ShokoFileId.Name, fileId.ToString() } },
                                        DtoOptions = new(true),
                                    },
                                    true
                                );
                            Logger.LogTrace("Found {Count} potential videos to remove", videos.Count);
                            foreach (var video in videos) {
                                if (string.IsNullOrEmpty(video.Path) || !video.Path.StartsWith(vfsPath) || result.Paths.Contains(video.Path)) {
                                    Logger.LogTrace("Skipped a {Kind} to remove with path {Path}", video.GetBaseItemKind(), video.Path);
                                    continue;
                                }
                                Logger.LogTrace("Found a {Kind} to remove with path {Path}", video.GetBaseItemKind(), video.Path);
                                RemoveSymbolicLink(video.Path);
                                topFolders.Add(Path.Join(vfsPath, video.Path[(vfsPath.Length + 1)..].Split(Path.DirectorySeparatorChar).First()));
                                locationsToNotify.Add(video.Path);
                                result.RemovedVideos++;
                            }

                            result.Print(Logger, mediaFolderPath);

                            // If all the "top-level-folders" exist, then let the core logic handle the rest.
                            if (topFolders.All(path => LibraryManager.FindByPath(path, true) is not null)) {
                                locationsToNotify.AddRange(vfsSymbolicLinks);
                            }
                            // Else give the core logic _any_ file or folder placed directly in the media folder, so it will schedule the media folder to be refreshed.
                            else {
                                var fileOrFolder = FileSystem.GetFileSystemEntryPaths(mainMediaFolderPath, false).FirstOrDefault();
                                if (!string.IsNullOrEmpty(fileOrFolder))
                                    mediaFoldersToNotify.TryAdd(mainMediaFolderPath, (fileOrFolder, mainMediaFolderPath.GetFolderForPath()));
                            }
                            break;
                        }
                    }
                }
            }

            if (LibraryScanWatcher.IsScanRunning) {
                Logger.LogDebug("Skipped notifying Jellyfin about {LocationCount} changes because a library scan is running. (File={FileId})", locationsToNotify.Count, fileId.ToString());
                return;
            }

            // We let jellyfin take it from here.
            Logger.LogDebug("Notifying Jellyfin about {LocationCount} changes. (File={FileId})", locationsToNotify.Count + mediaFoldersToNotify.Count, fileId.ToString());
            foreach (var location in locationsToNotify)
                LibraryMonitor.ReportFileSystemChanged(location);
            if (mediaFoldersToNotify.Count > 0)
                await Task.WhenAll(mediaFoldersToNotify.Values.Select(tuple => ReportMediaFolderChanged(tuple.mediaFolder, tuple.pathToReport))).ConfigureAwait(false);
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Error processing {EventCount} file change events. (File={FileId})", changes.Count, fileId);
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    private async Task<IReadOnlySet<string>> GetSeriesIdsForFile(int fileId, IFileEventArgs? fileEvent)
    {
        HashSet<string> seriesIds;
        if (fileEvent is not null && fileEvent.CrossReferences.All(xref => xref.ShokoSeriesId.HasValue && xref.ShokoEpisodeId.HasValue)) {
            seriesIds = fileEvent.CrossReferences.Select(xref => xref.ShokoSeriesId!.Value.ToString())
                .Distinct()
                .ToHashSet();
        }
        else {
            try {
                var file = await ApiClient.GetFile(fileId.ToString());
                seriesIds = file.CrossReferences
                    .Where(xref => xref.Series.Shoko.HasValue && xref.Episodes.All(e => e.Shoko.HasValue))
                    .Select(xref => xref.Series.Shoko!.Value.ToString())
                    .Distinct()
                    .ToHashSet();
            }
            catch (ApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound) {
                return new HashSet<string>();
            }
        }

        // TODO: Postpone the processing of the file if the episode or series is not available yet.

        var filteredSeriesIds = new HashSet<string>();
        foreach (var seriesId in seriesIds) {
            var (primaryId, extraIds) = await ApiManager.GetSeriesIdsForSeason(seriesId);
            var seriesPathSet = await ApiManager.GetPathSetForSeries(primaryId, extraIds);
            if (seriesPathSet.Count > 0) {
                filteredSeriesIds.Add(seriesId);
            }
        }

        // Return all series if we only have this file for all of them,
        // otherwise return only the series were we have other files that are
        // not linked to other series.
        return filteredSeriesIds.Count is 0 ? seriesIds : filteredSeriesIds;
    }

    private async Task<string?> GetNewSourceLocation(int importFolderId, string importFolderSubPath, int fileId, string relativePath, string mediaFolderPath)
    {
        // Check if the file still exists, and if it has any other locations we can use.
        try {
            var file = await ApiClient.GetFile(fileId.ToString());
            var usableLocation = file.Locations
                .Where(loc => loc.ImportFolderId == importFolderId && (string.IsNullOrEmpty(importFolderSubPath) || relativePath.StartsWith(importFolderSubPath + Path.DirectorySeparatorChar)) && loc.RelativePath != relativePath)
                .FirstOrDefault();
            if (usableLocation is null)
                return null;

            var sourceLocation = Path.Join(mediaFolderPath, usableLocation.RelativePath[importFolderSubPath.Length..]);
            if (!File.Exists(sourceLocation))
                return null;

            return sourceLocation;
        }
        catch (ApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound) {
            return null;
        }
    }

    private void RemoveSymbolicLink(string filePath)
    {
        // TODO: If this works better, the move it to an utility and also use it in the VFS if needed, or remove this comment if it's not needed.
        try {
            var fileExists = File.Exists(filePath);
            var fileInfo = new System.IO.FileInfo(filePath);
            var fileInfoExists = fileInfo.Exists;
            var reparseFlag = fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint);
            Logger.LogTrace(
                "Result for if file is a reparse point; {FilePath} (Exists1={FileExists},Exists2={FileInfoExists},ReparsePoint={IsReparsePoint},Attributes={AllAttributes})",
                filePath,
                fileExists,
                fileInfoExists,
                reparseFlag,
                fileInfo.Attributes
            );

            try {
                File.Delete(filePath);
            }
            catch (Exception ex) {
                Logger.LogError(ex, "Unable to remove symbolic link at path {Path}; {ErrorMessage}", filePath, ex.Message);
            }
        }
        catch (Exception ex) {
            Logger.LogTrace(ex, "Unable to check if file path exists and is a reparse point; {FilePath}", filePath);
        }
    }

    private async Task ReportMediaFolderChanged(Folder mediaFolder, string pathToReport)
    {
        // Don't block real-time file events on the media folder that uses a physical VFS root, or if real-time monitoring is disabled.
        if (mediaFolder.Path.StartsWith(Plugin.Instance.VirtualRoot) ||
            LibraryManager.GetLibraryOptions(mediaFolder) is not LibraryOptions libraryOptions ||
            !libraryOptions.EnableRealtimeMonitor
        ) {
            LibraryMonitor.ReportFileSystemChanged(pathToReport);
            return;
        }

        // Since we're blocking real-time file events on the media folder because
        // it uses the VFS then we need to temporarily unblock it, then block it
        // afterwards again.
        var path = mediaFolder.Path;
        var delayTime = TimeSpan.Zero;
        lock (MediaFolderChangeMonitor) {
            if (MediaFolderChangeMonitor.TryGetValue(path, out var entry)) {
                MediaFolderChangeMonitor[path] = (entry.refCount + 1, entry.delayEnd);
                delayTime = entry.delayEnd - DateTime.Now;
            }
            else {
                MediaFolderChangeMonitor[path] = (1, DateTime.Now + TimeSpan.FromMilliseconds(MagicalDelayValue));
                delayTime = TimeSpan.FromMilliseconds(MagicalDelayValue);
            }
        }

        LibraryMonitor.ReportFileSystemChangeComplete(path, false);

        if (delayTime > TimeSpan.Zero)
            await Task.Delay((int)delayTime.TotalMilliseconds).ConfigureAwait(false);

        LibraryMonitor.ReportFileSystemChanged(pathToReport);

        var shouldResume = false;
        lock (MediaFolderChangeMonitor) {
            if (MediaFolderChangeMonitor.TryGetValue(path, out var tuple)) {
                if (tuple.refCount is 1) {
                    shouldResume = true;
                    MediaFolderChangeMonitor.Remove(path);
                }
                else {
                    MediaFolderChangeMonitor[path] = (tuple.refCount - 1, tuple.delayEnd);
                }
            }
        }

        if (shouldResume)
            LibraryMonitor.ReportFileSystemChangeBeginning(path);
    }

    #endregion

    #region Refresh Events

    public void AddSeriesEvent(string metadataId, IMetadataUpdatedEventArgs eventArgs)
    {
        lock (ChangesPerSeries) {
            if (ChangesPerSeries.TryGetValue(metadataId, out var tuple))
                tuple.LastUpdated = DateTime.Now;
            else
                ChangesPerSeries.Add(metadataId, tuple = (DateTime.Now, [], Plugin.Instance.Tracker.Add($"Metadata event. (Reason=\"{eventArgs.Reason}\",Kind=\"{eventArgs.Kind}\",ProviderUId=\"{eventArgs.ProviderUId}\")")));
            tuple.List.Add(eventArgs);
        }
    }

    private async Task ProcessMetadataEvents(string metadataId, List<IMetadataUpdatedEventArgs> changes, Guid trackerId)
    {
        try {
            if (LibraryScanWatcher.IsScanRunning) {
                Logger.LogDebug("Skipped processing {EventCount} metadata change events because a library scan is running. (Metadata={ProviderUniqueId})", changes.Count, metadataId);
                return;
            }

            if (!changes.Any(e => e.Kind is BaseItemKind.Episode && e.EpisodeId.HasValue || e.Kind is BaseItemKind.Series && e.SeriesId.HasValue)) {
                Logger.LogDebug("Skipped processing {EventCount} metadata change events because no series or episode ids to use. (Metadata={ProviderUniqueId})", changes.Count, metadataId);
                return;
            }

            var seriesId = changes.First(e => e.SeriesId.HasValue).SeriesId!.Value.ToString();
            var showInfo = await ApiManager.GetShowInfoForSeries(seriesId);
            if (showInfo is null) {
                Logger.LogDebug("Unable to find show info for series id. (Series={SeriesId},Metadata={ProviderUniqueId})", seriesId, metadataId);
                return;
            }

            var seasonInfo = await ApiManager.GetSeasonInfoForSeries(seriesId);
            if (seasonInfo is null) {
                Logger.LogDebug("Unable to find season info for series id. (Series={SeriesId},Metadata={ProviderUniqueId})", seriesId, metadataId);
                return;
            }

            Logger.LogInformation("Processing {EventCount} metadata change events… (Metadata={ProviderUniqueId})", changes.Count, metadataId);

            var updateCount = await ProcessSeriesEvents(showInfo, changes);
            updateCount += await ProcessMovieEvents(seasonInfo, changes);

            Logger.LogInformation("Scheduled {UpdateCount} updates for {EventCount} metadata change events. (Metadata={ProviderUniqueId})", updateCount, changes.Count, metadataId);
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Error processing {EventCount} metadata change events. (Metadata={ProviderUniqueId})", changes.Count, metadataId);
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    private async Task<int> ProcessSeriesEvents(ShowInfo showInfo, List<IMetadataUpdatedEventArgs> changes)
    {
        // Update the series if we got a series event _or_ an episode removed event.
        var updateCount = 0;
        var animeEvent = changes.Find(e => e.Kind is BaseItemKind.Series || e.Kind is BaseItemKind.Episode && e.Reason is UpdateReason.Removed);
        if (animeEvent is not null) {
            var shows = LibraryManager
                .GetItemList(
                    new() {
                        IncludeItemTypes = [BaseItemKind.Series],
                        HasAnyProviderId = new Dictionary<string, string> { { ShokoSeriesId.Name, showInfo.Id } },
                        DtoOptions = new(true),
                    },
                    true
                )
                .ToList();
            foreach (var show in shows) {
                Logger.LogInformation("Refreshing show {ShowName}. (Show={ShowId},Series={SeriesId})", show.Name, show.Id, showInfo.Id);
                await show.RefreshMetadata(new(DirectoryService) {
                    MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                    ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                    ReplaceAllMetadata = true,
                    ReplaceAllImages = true,
                    RemoveOldMetadata = true,
                    ReplaceImages = Enum.GetValues<ImageType>().ToArray(),
                    IsAutomated = true,
                    EnableRemoteContentProbe = true,
                }, CancellationToken.None);
                updateCount++;
            }
        }
        // Otherwise update all season/episodes where appropriate.
        else {
            var episodeIds = changes
                .Where(e => e.EpisodeId.HasValue && e.Reason is not UpdateReason.Removed)
                .Select(e => e.EpisodeId!.Value.ToString())
                .ToHashSet();
            var seasonIds = changes
                .Where(e => e.EpisodeId.HasValue && e.SeriesId.HasValue && e.Reason is UpdateReason.Removed)
                .Select(e => e.SeriesId!.Value.ToString())
                .ToHashSet();
            var seasonList = showInfo.SeasonList
                .Where(seasonInfo => seasonIds.Contains(seasonInfo.Id) || seasonIds.Overlaps(seasonInfo.ExtraIds))
                .ToList();
            foreach (var seasonInfo in seasonList) {
                var seasons = LibraryManager
                    .GetItemList(
                        new() {
                            IncludeItemTypes = [BaseItemKind.Season],
                            HasAnyProviderId = new Dictionary<string, string> { { ShokoSeriesId.Name, seasonInfo.Id } },
                            DtoOptions = new(true),
                        },
                        true
                    )
                    .ToList();
                foreach (var season in seasons) {
                    Logger.LogInformation("Refreshing season {SeasonName}. (Season={SeasonId},Series={SeriesId},ExtraSeries={ExtraIds})", season.Name, season.Id, seasonInfo.Id, seasonInfo.ExtraIds);
                    await season.RefreshMetadata(new(DirectoryService) {
                        MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                        ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                        ReplaceAllMetadata = true,
                        ReplaceAllImages = true,
                        RemoveOldMetadata = true,
                        ReplaceImages = Enum.GetValues<ImageType>().ToArray(),
                        IsAutomated = true,
                        EnableRemoteContentProbe = true,
                    }, CancellationToken.None);
                    updateCount++;
                }
            }
            var episodeList = showInfo.SeasonList
                .Except(seasonList)
                .SelectMany(seasonInfo => seasonInfo.EpisodeList.Concat(seasonInfo.AlternateEpisodesList).Concat(seasonInfo.SpecialsList))
                .Where(episodeInfo => episodeIds.Contains(episodeInfo.Id))
                .ToList();
            foreach (var episodeInfo in episodeList) {
                var episodes = LibraryManager
                    .GetItemList(
                        new() {
                            IncludeItemTypes = [BaseItemKind.Episode],
                            HasAnyProviderId = new Dictionary<string, string> { { ShokoEpisodeId.Name, episodeInfo.Id } },
                            DtoOptions = new(true),
                        },
                        true
                    )
                    .ToList();
                foreach (var episode in episodes) {
                    Logger.LogInformation("Refreshing episode {EpisodeName}. (Episode={EpisodeId},Episode={EpisodeId},Series={SeriesId})", episode.Name, episode.Id, episodeInfo.Id, episodeInfo.Shoko.IDs.ParentSeries.ToString());
                    await episode.RefreshMetadata(new(DirectoryService) {
                        MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                        ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                        ReplaceAllMetadata = true,
                        ReplaceAllImages = true,
                        RemoveOldMetadata = true,
                        ReplaceImages = Enum.GetValues<ImageType>().ToArray(),
                        IsAutomated = true,
                        EnableRemoteContentProbe = true,
                    }, CancellationToken.None);
                    updateCount++;
                }
            }
        }
        return updateCount;
    }

    private async Task<int> ProcessMovieEvents(SeasonInfo seasonInfo, List<IMetadataUpdatedEventArgs> changes)
    {
        // Find movies and refresh them.
        var updateCount = 0;
        var episodeIds = changes
            .Where(e => e.EpisodeId.HasValue && e.Reason is not UpdateReason.Removed)
            .Select(e => e.EpisodeId!.Value.ToString())
            .ToHashSet();
        var episodeList = seasonInfo.EpisodeList
            .Concat(seasonInfo.AlternateEpisodesList)
            .Concat(seasonInfo.SpecialsList)
            .Where(episodeInfo => episodeIds.Contains(episodeInfo.Id))
            .ToList();
        foreach (var episodeInfo in episodeList) {
            var movies = LibraryManager
                .GetItemList(
                    new() {
                        IncludeItemTypes = [BaseItemKind.Movie],
                        HasAnyProviderId = new Dictionary<string, string> { { ShokoEpisodeId.Name, episodeInfo.Id } },
                        DtoOptions = new(true),
                    },
                    true
                )
                .ToList();
            foreach (var movie in movies) {
                Logger.LogInformation("Refreshing movie {MovieName}. (Movie={MovieId},Episode={EpisodeId},Series={SeriesId},ExtraSeries={ExtraIds})", movie.Name, movie.Id, episodeInfo.Id, seasonInfo.Id, seasonInfo.ExtraIds);
                await movie.RefreshMetadata(new(DirectoryService) {
                    MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                    ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                    ReplaceAllMetadata = true,
                    ReplaceAllImages = true,
                    RemoveOldMetadata = true,
                    ReplaceImages = Enum.GetValues<ImageType>().ToArray(),
                    IsAutomated = true,
                    EnableRemoteContentProbe = true,
                }, CancellationToken.None);
                updateCount++;
            }
        }
        return updateCount;
    }

    #endregion
}