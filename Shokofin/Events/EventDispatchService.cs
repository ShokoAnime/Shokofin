using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Info;
using Shokofin.Configuration;
using Shokofin.Events.Interfaces;
using Shokofin.Extensions;
using Shokofin.ExternalIds;
using Shokofin.Resolvers;
using Shokofin.Resolvers.Models;
using Shokofin.Utils;

using File = System.IO.File;
using Movie = MediaBrowser.Controller.Entities.Movies.Movie;
using Timer = System.Timers.Timer;
using TvEpisode = MediaBrowser.Controller.Entities.TV.Episode;
using TvSeason = MediaBrowser.Controller.Entities.TV.Season;
using TvSeries = MediaBrowser.Controller.Entities.TV.Series;

namespace Shokofin.Events;

public class EventDispatchService {
    private readonly ShokoApiManager ApiManager;

    private readonly ShokoApiClient ApiClient;

    private readonly ILibraryManager LibraryManager;

    private readonly ILibraryMonitor LibraryMonitor;

    private readonly LibraryScanWatcher LibraryScanWatcher;

    private readonly MetadataRefreshService MetadataRefreshService;

    private readonly MediaFolderConfigurationService ConfigurationService;

    private readonly VirtualFileSystemService ResolveManager;

    private readonly IFileSystem FileSystem;

    private readonly ILogger<EventDispatchService> Logger;

    private readonly UsageTracker UsageTracker;

    private int ChangesDetectionSubmitterCount = 0;

    private readonly Timer ChangesDetectionTimer;

    private readonly Dictionary<string, (DateTime LastUpdated, List<IMetadataUpdatedEventArgs> List, Guid trackerId)> ChangesPerSeries = [];

    private readonly Dictionary<int, (DateTime LastUpdated, List<(UpdateReason Reason, int ManagedFolderId, string Path, IFileEventArgs Event)> List, Guid trackerId)> ChangesPerFile = [];

    private readonly Dictionary<string, (int refCount, DateTime delayEnd)> MediaFolderChangeMonitor = [];

    // It's so magical that it matches the magical value in the library monitor in JF core. 🪄
    private const int MagicalDelayValue = 45000;

    private readonly ConcurrentDictionary<Guid, bool> RecentlyUpdatedEntitiesDict = new();

    private static readonly TimeSpan DetectChangesThreshold = TimeSpan.FromSeconds(5);

    public EventDispatchService(
        ShokoApiManager apiManager,
        ShokoApiClient apiClient,
        ILibraryManager libraryManager,
        ILibraryMonitor libraryMonitor,
        LibraryScanWatcher libraryScanWatcher,
        MetadataRefreshService metadataRefreshService,
        MediaFolderConfigurationService configurationService,
        VirtualFileSystemService resolveManager,
        IFileSystem fileSystem,
        ILogger<EventDispatchService> logger,
        UsageTracker usageTracker
    ) {
        ApiManager = apiManager;
        ApiClient = apiClient;
        LibraryManager = libraryManager;
        LibraryMonitor = libraryMonitor;
        LibraryScanWatcher = libraryScanWatcher;
        MetadataRefreshService = metadataRefreshService;
        ConfigurationService = configurationService;
        ResolveManager = resolveManager;
        FileSystem = fileSystem;
        Logger = logger;
        UsageTracker = usageTracker;
        UsageTracker.Stalled += OnStalled;
        ChangesDetectionTimer = new() { AutoReset = true, Interval = TimeSpan.FromSeconds(4).TotalMilliseconds };
        ChangesDetectionTimer.Elapsed += OnIntervalElapsed;
    }

    ~EventDispatchService() {
        UsageTracker.Stalled -= OnStalled;
        ChangesDetectionTimer.Elapsed -= OnIntervalElapsed;
    }

    private void OnStalled(object? sender, EventArgs eventArgs) {
        Clear();
    }

    public void Clear() => RecentlyUpdatedEntitiesDict.Clear();

    #region Event Detection

    public IDisposable RegisterEventSubmitter() {
        var count = ChangesDetectionSubmitterCount++;
        if (count is 0)
            ChangesDetectionTimer.Start();

        return new DisposableAction(() => DeregisterEventSubmitter());
    }

    private void DeregisterEventSubmitter() {
        var count = --ChangesDetectionSubmitterCount;
        if (count is 0) {
            ChangesDetectionTimer.Stop();
            if (ChangesPerFile.Count > 0)
                ClearFileEvents();
            if (ChangesPerSeries.Count > 0)
                ClearMetadataUpdatedEvents();
        }
    }

    private void OnIntervalElapsed(object? sender, ElapsedEventArgs eventArgs) {
        var filesToProcess = new List<(int, List<(UpdateReason Reason, int ManagedFolderId, string Path, IFileEventArgs Event)>, Guid trackerId)>();
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

    private void ClearFileEvents() {
        var filesToProcess = new List<(int, List<(UpdateReason Reason, int ManagedFolderId, string Path, IFileEventArgs Event)>, Guid trackerId)>();
        lock (ChangesPerFile) {
            foreach (var (fileId, (lastUpdated, list, trackerId)) in ChangesPerFile) {
                filesToProcess.Add((fileId, list, trackerId));
            }
            ChangesPerFile.Clear();
        }
        foreach (var (fileId, changes, trackerId) in filesToProcess)
            Task.Run(() => ProcessFileEvents(fileId, changes, trackerId));
    }

    private void ClearMetadataUpdatedEvents() {
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

    public void AddFileEvent(int fileId, UpdateReason reason, int managedFolderId, string filePath, IFileEventArgs eventArgs) {
        lock (ChangesPerFile) {
            if (ChangesPerFile.TryGetValue(fileId, out var tuple))
                tuple.LastUpdated = DateTime.Now;
            else
                ChangesPerFile.Add(fileId, tuple = (DateTime.Now, [], Plugin.Instance.Tracker.Add($"File event. (Reason=\"{reason}\",ManagedFolder={eventArgs.ManagedFolderId},RelativePath=\"{eventArgs.RelativePath}\")")));
            tuple.List.Add((reason, managedFolderId, filePath, eventArgs));
        }
    }

    private async Task ProcessFileEvents(int fileId, List<(UpdateReason Reason, int ManagedFolderId, string Path, IFileEventArgs Event)> changes, Guid trackerId) {
        try {
            if (LibraryScanWatcher.IsScanRunning) {
                Logger.LogInformation("Skipped processing {EventCount} file change events because a library scan is running. (File={FileId})", changes.Count, fileId);
                return;
            }

            Logger.LogInformation("Processing {EventCount} file change events… (File={FileId})", changes.Count, fileId);

            // Something was added or updated.
            var locationsToNotify = new List<string>();
            var seriesIds = await GetSeriesIdsForFile(fileId, changes.Select(t => t.Event).LastOrDefault(e => e.HasCrossReferences)).ConfigureAwait(false);
            var libraries = await ConfigurationService.GetAvailableMediaFoldersForLibraries(c => c.Library.IsFileEventsEnabled).ConfigureAwait(false);
            var (reason, managedFolderId, relativePath, lastEvent) = changes.Last();
            if (reason is not UpdateReason.MetadataRemoved) {
                Logger.LogTrace("Processing file changed. (File={FileId})", fileId);
                foreach (var (vfsPath, collectionType, mediaConfigs) in libraries) {
                    foreach (var (managedFolderSubPath, vfsEnabled, mediaFolderPaths) in mediaConfigs.ToManagedFolderList(managedFolderId, relativePath)) {
                        foreach (var mediaFolderPath in mediaFolderPaths) {
                            var sourceLocation = Path.Join(mediaFolderPath, relativePath[managedFolderSubPath.Length..]);
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
                                result += ResolveManager.GenerateSymbolicLinks(vfsPath, sourceLocation, symLinks, importDate!.Value);
                                foreach (var path in symLinks.Select(path => Path.Join(vfsPath, path[(vfsPath.Length + 1)..].Split(Path.DirectorySeparatorChar).First())).Distinct())
                                    topFolders.Add(path);
                            }

                            // Remove old links for file.
                            var videos = LibraryManager
                                .GetItemList(
                                    new() {
                                        SourceTypes = [SourceType.Library],
                                        HasAnyProviderId = new Dictionary<string, string> { { ProviderNames.ShokoFile, fileId.ToString() } },
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

                            locationsToNotify.AddRange(vfsLocations.SelectMany(tuple => tuple.symbolicLinks));
                            break;
                        }
                    }
                }
            }
            // Something was removed, so assume the location is gone.
            else if (changes.FirstOrDefault(t => t.Reason is UpdateReason.MetadataRemoved).Event is IFileEventArgs firstRemovedEvent) {
                // If we don't know which series to remove, then add all of them to be scanned.
                if (seriesIds.Count is 0) {
                    Logger.LogTrace("No series found for file. Adding all libraries. (File={FileId})", fileId);
                    foreach (var (vfsPath, collectionType, mediaConfigs) in libraries) {
                        locationsToNotify.Add(vfsPath);
                    }

                    goto aLabelToReduceNesting;
                }

                Logger.LogTrace("Processing file removed. (File={FileId})", fileId);
                relativePath = firstRemovedEvent.RelativePath;
                managedFolderId = firstRemovedEvent.ManagedFolderId;
                foreach (var (vfsPath, collectionType, mediaConfigs) in libraries) {
                    foreach (var (managedFolderSubPath, vfsEnabled, mediaFolderPaths) in mediaConfigs.ToManagedFolderList(managedFolderId, relativePath)) {
                        foreach (var mediaFolderPath in mediaFolderPaths) {
                            // Let the core logic handle the rest.
                            if (!vfsEnabled) {
                                var sourceLocation = Path.Join(mediaFolderPath, relativePath[managedFolderSubPath.Length..]);
                                locationsToNotify.Add(sourceLocation);
                                break;
                            }

                            // Check if we can use another location for the file.
                            var result = new LinkGenerationResult();
                            var vfsSymbolicLinks = new HashSet<string>();
                            var topFolders = new HashSet<string>();
                            var newSourceLocation = await GetNewSourceLocation(managedFolderId, managedFolderSubPath, fileId, relativePath, mediaFolderPath).ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(newSourceLocation)) {
                                var vfsLocations = (await Task.WhenAll(seriesIds.Select(seriesId => ResolveManager.GenerateLocationsForFile(collectionType, vfsPath, newSourceLocation, fileId.ToString(), seriesId))).ConfigureAwait(false))
                                .Where(tuple => tuple.symbolicLinks.Length > 0 && tuple.importedAt.HasValue)
                                    .ToList();
                                foreach (var (symLinks, importDate) in vfsLocations) {
                                    result += ResolveManager.GenerateSymbolicLinks(vfsPath, newSourceLocation, symLinks, importDate!.Value);
                                    foreach (var path in symLinks.Select(path => Path.Join(vfsPath, path[(vfsPath.Length + 1)..].Split(Path.DirectorySeparatorChar).First())).Distinct())
                                        topFolders.Add(path);
                                }
                                vfsSymbolicLinks = vfsLocations.SelectMany(tuple => tuple.symbolicLinks).ToHashSet();
                            }

                            // Remove old links for file.
                            var videos = LibraryManager
                                .GetItemList(
                                    new() {
                                        SourceTypes = [SourceType.Library],
                                        HasAnyProviderId = new Dictionary<string, string> { { ProviderNames.ShokoFile, fileId.ToString() } },
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

                            locationsToNotify.AddRange(vfsSymbolicLinks);
                            break;
                        }
                    }
                }
            }

            aLabelToReduceNesting:;
            if (LibraryScanWatcher.IsScanRunning) {
                Logger.LogDebug("Skipped notifying Jellyfin about {LocationCount} changes because a library scan is running. (File={FileId})", locationsToNotify.Count, fileId.ToString());
                return;
            }

            // We let jellyfin take it from here.
            Logger.LogDebug("Notifying Jellyfin about {LocationCount} changes. (File={FileId})", locationsToNotify.Count, fileId.ToString());
            foreach (var location in locationsToNotify) {
                Logger.LogTrace("Notifying Jellyfin about changes to {Location}. (File={FileId})", location, fileId.ToString());
                LibraryMonitor.ReportFileSystemChanged(location);
            }
            Logger.LogDebug("Notified Jellyfin about {LocationCount} changes. (File={FileId})", locationsToNotify.Count, fileId.ToString());
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Error processing {EventCount} file change events. (File={FileId})", changes.Count, fileId);
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    private async Task<IReadOnlySet<string>> GetSeriesIdsForFile(int fileId, IFileEventArgs? fileEvent) {
        HashSet<string> seriesIds;
        if (fileEvent is not null && fileEvent.CrossReferences.All(xref => xref.ShokoSeriesId.HasValue && xref.ShokoEpisodeId.HasValue)) {
            seriesIds = fileEvent.CrossReferences.Select(xref => xref.ShokoSeriesId!.Value.ToString())
                .Distinct()
                .ToHashSet();
        }
        else {
            var file = await ApiClient.GetFile(fileId.ToString()).ConfigureAwait(false);
            if (file is null)
                return new HashSet<string>();

            seriesIds = file.CrossReferences
                .Where(xref => xref.Series.Shoko.HasValue && xref.Episodes.All(e => e.Shoko.HasValue))
                .Select(xref => xref.Series.Shoko!.Value.ToString())
                .Distinct()
                .ToHashSet();
        }

        // TODO: Postpone the processing of the file if the episode or series is not available yet.

        var filteredSeriesIds = new HashSet<string>();
        foreach (var seriesId in seriesIds) {
            var (primaryId, extraIds) = await ApiManager.GetSeriesIdsForShokoSeries(seriesId).ConfigureAwait(false);
            if (await ApiManager.GetPathSetForSeries(primaryId).ConfigureAwait(false) is { Count: > 0 }) {
                filteredSeriesIds.Add(seriesId);
            }
            else if (extraIds.Count > 0) {
                foreach (var extraId in extraIds) {
                    if (await ApiManager.GetPathSetForSeries(extraId).ConfigureAwait(false) is { Count: > 0 }) {
                        filteredSeriesIds.Add(seriesId);
                        break;
                    }
                }
            }
        }

        // Return all series if we only have this file for all of them,
        // otherwise return only the series where we have other files that are
        // not linked to other series.
        return filteredSeriesIds.Count is 0 ? seriesIds : filteredSeriesIds;
    }

    private async Task<string?> GetNewSourceLocation(int managedFolderId, string managedFolderSubPath, int fileId, string relativePath, string mediaFolderPath) {
        // Check if the file still exists, and if it has any other locations we can use.
        var file = await ApiClient.GetFile(fileId.ToString()).ConfigureAwait(false);
        if (file is null)
            return null;

        var usableLocation = file.Locations
            .Where(loc => loc.ManagedFolderId == managedFolderId && (string.IsNullOrEmpty(managedFolderSubPath) || relativePath.StartsWith(managedFolderSubPath + Path.DirectorySeparatorChar)) && loc.RelativePath != relativePath)
            .FirstOrDefault();
        if (usableLocation is null)
            return null;

        var sourceLocation = Path.Join(mediaFolderPath, usableLocation.RelativePath[managedFolderSubPath.Length..]);
        if (!File.Exists(sourceLocation))
            return null;

        return sourceLocation;
    }

    private void RemoveSymbolicLink(string filePath) {
        // TODO: If this works better, then move it to an utility and also use it in the VFS if needed, or remove this comment if that's not needed.
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

    #endregion

    #region Refresh Events

    public void AddSeriesEvent(string metadataId, IMetadataUpdatedEventArgs eventArgs) {
        lock (ChangesPerSeries) {
            if (ChangesPerSeries.TryGetValue(metadataId, out var tuple))
                tuple.LastUpdated = DateTime.Now;
            else
                ChangesPerSeries.Add(metadataId, tuple = (DateTime.Now, [], Plugin.Instance.Tracker.Add($"Metadata event. (Reason=\"{eventArgs.Reason}\",Kind=\"{eventArgs.Kind}\",ProviderUId=\"{eventArgs.ProviderUId}\")")));
            tuple.List.Add(eventArgs);
        }
    }

    private async Task ProcessMetadataEvents(string metadataId, List<IMetadataUpdatedEventArgs> events, Guid trackerId) {
        try {
            var tasks = new List<Task>();
            if (events.Where(e => e.IsImageUpdate).ToList() is { Count: > 0 } imageEvents)
                tasks.Add(ProcessImageUpdateEvents(metadataId, imageEvents));
            if (events.Where(e => e.IsMetadataUpdate).ToList() is { Count: > 0 } metadataEvents)
                tasks.Add(ProcessMetadataUpdateEvents(metadataId, metadataEvents));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    private async Task ProcessImageUpdateEvents(string metadataId, List<IMetadataUpdatedEventArgs> changes) {
        try {
            if (!changes.Any(e => e.Kind is BaseItemKind.Episode or BaseItemKind.Movie && e.EpisodeIds.Count > 0 || e.Kind is BaseItemKind.Series && e.SeriesIds.Count > 0)) {
                Logger.LogDebug("Skipped processing {EventCount} image change events because no series or episode ids to use. (Metadata={ProviderUniqueId})", changes.Count, metadataId);
                return;
            }

            var allSeriesIds = changes.SelectMany(e => e.SeriesIds).ToHashSet();
            var seasonInfoDict = new Dictionary<string, SeasonInfo>();
            var seriesIdDict = new Dictionary<int, string[]>();
            foreach (var seriesId in allSeriesIds) {
                var seasonInfoList = await ApiManager.GetSeasonInfosForShokoSeries(seriesId.ToString()).ConfigureAwait(false);
                foreach (var seasonInfo in seasonInfoList) {
                    seasonInfoDict.Add(seasonInfo.Id, seasonInfo);
                }
                seriesIdDict.Add(seriesId, seasonInfoList.Select(s => s.Id).ToArray());
            }

            if (seasonInfoDict.Count is 0) {
                Logger.LogDebug("Unable to find season info for series id. (Metadata={ProviderUniqueId})", metadataId);
                return;
            }

            var showInfoList = (await Task.WhenAll(seasonInfoDict.Values.Select(s => ApiManager.GetShowInfoBySeasonId(s.Id))).ConfigureAwait(false))
                .WhereNotNull()
                .DistinctBy(s => s.Id)
                .ToList();
            if (showInfoList.Count is 0) {
                Logger.LogDebug("Unable to find show info for series id. (Metadata={ProviderUniqueId})", metadataId);
                return;
            }

            Logger.LogInformation("Processing {EventCount} image change events… (Metadata={ProviderUniqueId})", changes.Count, metadataId);

            var updateCount = 0;
            var refreshFieldsMask = MetadataRefreshField.Images | MetadataRefreshField.PreferredImages;
            foreach (var showInfo in showInfoList)
                updateCount += await ProcessSeriesEvents(showInfo, changes, seriesIdDict, refreshFieldsMask).ConfigureAwait(false);

            foreach (var seasonInfo in seasonInfoDict.Values)
                updateCount += await ProcessMovieEvents(seasonInfo, changes, refreshFieldsMask).ConfigureAwait(false);

            Logger.LogInformation("Scheduled {UpdateCount} image updates for {EventCount} image change events. (Metadata={ProviderUniqueId})", updateCount, changes.Count, metadataId);
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Error processing {EventCount} image change events. (Metadata={ProviderUniqueId})", changes.Count, metadataId);
        }
    }

    private async Task ProcessMetadataUpdateEvents(string metadataId, List<IMetadataUpdatedEventArgs> changes) {
        try {
            if (LibraryScanWatcher.IsScanRunning) {
                Logger.LogDebug("Skipped processing {EventCount} metadata change events because a library scan is running. (Metadata={ProviderUniqueId})", changes.Count, metadataId);
                return;
            }

            if (!changes.Any(e => e.Kind is BaseItemKind.Episode or BaseItemKind.Movie && e.EpisodeIds.Count > 0 || e.Kind is BaseItemKind.Series && e.SeriesIds.Count > 0)) {
                Logger.LogDebug("Skipped processing {EventCount} metadata change events because no series or episode ids to use. (Metadata={ProviderUniqueId})", changes.Count, metadataId);
                return;
            }

            var allSeriesIds = changes.SelectMany(e => e.SeriesIds).ToHashSet();
            var seasonInfoDict = new Dictionary<string, SeasonInfo>();
            var seriesIdDict = new Dictionary<int, string[]>();
            foreach (var seriesId in allSeriesIds) {
                var seasonInfoList = await ApiManager.GetSeasonInfosForShokoSeries(seriesId.ToString()).ConfigureAwait(false);
                foreach (var seasonInfo in seasonInfoList) {
                    seasonInfoDict.Add(seasonInfo.Id, seasonInfo);
                }
                seriesIdDict.Add(seriesId, seasonInfoList.Select(s => s.Id).ToArray());
            }

            if (seasonInfoDict.Count is 0) {
                Logger.LogDebug("Unable to find season info for series id. (Metadata={ProviderUniqueId})", metadataId);
                return;
            }

            var showInfoList = (await Task.WhenAll(seasonInfoDict.Values.Select(s => ApiManager.GetShowInfoBySeasonId(s.Id))).ConfigureAwait(false))
                .WhereNotNull()
                .DistinctBy(s => s.Id)
                .ToList();
            if (showInfoList.Count is 0) {
                Logger.LogDebug("Unable to find show info for series id. (Metadata={ProviderUniqueId})", metadataId);
                return;
            }

            Logger.LogInformation("Processing {EventCount} metadata change events… (Metadata={ProviderUniqueId})", changes.Count, metadataId);

            var updateCount = 0;
            var refreshFieldsMask = ~(MetadataRefreshField.Images | MetadataRefreshField.PreferredImages);
            foreach (var showInfo in showInfoList)
                updateCount += await ProcessSeriesEvents(showInfo, changes, seriesIdDict, refreshFieldsMask).ConfigureAwait(false);

            foreach (var seasonInfo in seasonInfoDict.Values)
                updateCount += await ProcessMovieEvents(seasonInfo, changes, refreshFieldsMask).ConfigureAwait(false);

            Logger.LogInformation("Scheduled {UpdateCount} metadata updates for {EventCount} metadata change events. (Metadata={ProviderUniqueId})", updateCount, changes.Count, metadataId);
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Error processing {EventCount} metadata change events. (Metadata={ProviderUniqueId})", changes.Count, metadataId);
        }
    }

    private async Task<int> ProcessSeriesEvents(ShowInfo showInfo, List<IMetadataUpdatedEventArgs> changes, IReadOnlyDictionary<int, string[]> seriesIdDict, MetadataRefreshField refreshFieldsMask) {
        // Update the series if we got a series event.
        var updateCount = 0;
        if (changes.Find(e => e.Kind is BaseItemKind.Series) is not null) {
            var shows = LibraryManager
                .GetItemList(new() {
                    IncludeItemTypes = [BaseItemKind.Series],
                    SourceTypes = [SourceType.Library],
                    HasAnyProviderId = new Dictionary<string, string> { { ShokoInternalId.Name, showInfo.InternalId } },
                    DtoOptions = new(true),
                })
                .DistinctBy(s => s.Id)
                .OfType<TvSeries>()
                .ToList();
            foreach (var show in shows) {
                if (!RecentlyUpdatedEntitiesDict.TryAdd(show.Id, true)) {
                    Logger.LogTrace("Show {ShowName} is already being updated. (Show={ShowId},Series={SeriesId})", show.Name, show.Id, showInfo.Id);
                    continue;
                }

                Logger.LogInformation("Refreshing show {ShowName}. (Show={ShowId},Series={SeriesId})", show.Name, show.Id, showInfo.Id);
                await MetadataRefreshService.RefreshSeries(show, refreshFieldsMask).ConfigureAwait(false);
                updateCount++;
            }
        }
        // Otherwise update all season/episodes where appropriate.
        else {
            var episodeIds = changes
                .Where(e => e.EpisodeIds.Count > 0 && e.Reason is not UpdateReason.MetadataRemoved)
                .SelectMany(e => new List<string>([
                    ..e.EpisodeIds.Select(eI => eI.ToString()),
                    ..(e.Kind is BaseItemKind.Movie && e.ProviderName is ProviderName.TMDB) ? [IdPrefix.TmdbMovie + e.ProviderId] : Array.Empty<string>(),
                    ..(e.Kind is BaseItemKind.Episode && e.ProviderName is ProviderName.TMDB) ? [IdPrefix.TmdbShow + e.ProviderId] : Array.Empty<string>(),
                ]))
                .ToHashSet();
            var seasonIds = changes
                .Where(e => e.EpisodeIds.Count > 0 && e.SeriesIds.Count > 0 && e.Reason is UpdateReason.MetadataRemoved)
                .SelectMany(e => e.SeriesIds.SelectMany(s => seriesIdDict[s]))
                .ToHashSet();
            var seasonList = showInfo.SeasonList
                .Where(seasonInfo => seasonIds.Contains(seasonInfo.Id) || seasonIds.Overlaps(seasonInfo.ExtraIds))
                .ToList();
            foreach (var seasonInfo in seasonList) {
                var seasons = LibraryManager
                    .GetItemList(new() {
                        IncludeItemTypes = [BaseItemKind.Season],
                        SourceTypes = [SourceType.Library],
                        HasAnyProviderId = new Dictionary<string, string> { { ShokoInternalId.Name, seasonInfo.InternalId } },
                        DtoOptions = new(true),
                    })
                    .DistinctBy(s => s.Id)
                    .OfType<TvSeason>()
                    .ToList();
                foreach (var season in seasons) {
                    var showId = season.SeriesId;
                    if (RecentlyUpdatedEntitiesDict.ContainsKey(showId)) {
                        Logger.LogTrace("Show is already being updated. (Check=1,Show={ShowId},TvSeason={SeasonId},Season={SeasonId})", showId, season.Id, seasonInfo.Id);
                        continue;
                    }

                    if (!RecentlyUpdatedEntitiesDict.TryAdd(season.Id, true)) {
                        Logger.LogTrace("Season is already being updated. (Check=2,Show={ShowId},TvSeason={SeasonId},Season={SeasonId})", showId, season.Id, seasonInfo.Id);
                        continue;
                    }

                    Logger.LogInformation("Refreshing season {SeasonName}. (TvSeason={SeasonId},Season={SeasonId},ExtraSeries={ExtraIds})", season.Name, season.Id, seasonInfo.Id, seasonInfo.ExtraIds);
                    await MetadataRefreshService.RefreshSeason(season, refreshFieldsMask).ConfigureAwait(false);
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
                    .GetItemList(new() {
                        IncludeItemTypes = [BaseItemKind.Episode],
                        SourceTypes = [SourceType.Library],
                        HasAnyProviderId = new Dictionary<string, string> { { ProviderNames.ShokoEpisode, episodeInfo.Id } },
                        DtoOptions = new(true),
                    })
                    .DistinctBy(e => e.Id)
                    .OfType<TvEpisode>()
                    .ToList();
                foreach (var episode in episodes) {
                    var showId = episode.SeriesId;
                    var seasonId = episode.SeasonId;
                    if (RecentlyUpdatedEntitiesDict.ContainsKey(showId)) {
                        Logger.LogTrace("Show is already being updated. (Check=1,Show={ShowId},Season={SeasonId},Episode={EpisodeId},Episode={EpisodeId},Season={SeasonId})", showId, seasonId, episode.Id, episodeInfo.Id, episodeInfo.SeasonId);
                        continue;
                    }

                    if (RecentlyUpdatedEntitiesDict.ContainsKey(seasonId)) {
                        Logger.LogTrace("Season is already being updated. (Check=2,Show={ShowId},Season={SeasonId},Episode={EpisodeId},Episode={EpisodeId},Season={SeasonId})", showId, seasonId, episode.Id, episodeInfo.Id, episodeInfo.SeasonId);
                        continue;
                    }

                    if (!RecentlyUpdatedEntitiesDict.TryAdd(episode.Id, true)) {
                        Logger.LogTrace("Episode is already being updated. (Check=3,Show={ShowId},Season={SeasonId},Episode={EpisodeId},Episode={EpisodeId},Season={SeasonId})", showId, seasonId, episode.Id, episodeInfo.Id, episodeInfo.SeasonId);
                        continue;
                    }

                    Logger.LogInformation("Refreshing episode {EpisodeName}. (Episode={EpisodeId},Episode={EpisodeId},Season={SeasonId})", episode.Name, episode.Id, episodeInfo.Id, episodeInfo.SeasonId);
                    await MetadataRefreshService.RefreshEpisode(episode, refreshFieldsMask).ConfigureAwait(false);
                    updateCount++;
                }
            }
        }
        return updateCount;
    }

    private async Task<int> ProcessMovieEvents(SeasonInfo seasonInfo, List<IMetadataUpdatedEventArgs> changes, MetadataRefreshField refreshFieldsMask) {
        // Find movies and refresh them.
        var updateCount = 0;
        var episodeIds = changes
            .Where(e => e.EpisodeIds.Count > 0 && e.Reason is not UpdateReason.MetadataRemoved)
            .SelectMany(e => new List<string>([
                ..e.EpisodeIds.Select(eI => eI.ToString()),
                ..(e.Kind is BaseItemKind.Movie && e.ProviderName is ProviderName.TMDB) ? [IdPrefix.TmdbMovie + e.ProviderId.ToString()] : Array.Empty<string>(),
                ..(e.Kind is BaseItemKind.Episode && e.ProviderName is ProviderName.TMDB) ? [IdPrefix.TmdbShow + e.ProviderId.ToString()] : Array.Empty<string>(),
            ]))
            .ToHashSet();
        var episodeList = seasonInfo.EpisodeList
            .Concat(seasonInfo.AlternateEpisodesList)
            .Concat(seasonInfo.SpecialsList)
            .Where(episodeInfo => episodeIds.Contains(episodeInfo.Id))
            .ToList();
        foreach (var episodeInfo in episodeList) {
            var movies = LibraryManager
                .GetItemList(new() {
                    IncludeItemTypes = [BaseItemKind.Movie],
                    SourceTypes = [SourceType.Library],
                    HasAnyProviderId = new Dictionary<string, string> { { ProviderNames.ShokoEpisode, episodeInfo.Id } },
                    DtoOptions = new(true),
                })
                .DistinctBy(e => e.Id)
                .OfType<Movie>()
                .ToList();
            foreach (var movie in movies) {
                if (!RecentlyUpdatedEntitiesDict.TryAdd(movie.Id, true)) {
                    Logger.LogTrace("Movie is already being updated. (Movie={MovieId},Episode={EpisodeId},Season={SeasonId},ExtraSeasons={ExtraIds})", movie.Id, episodeInfo.Id, seasonInfo.Id, seasonInfo.ExtraIds);
                    continue;
                }

                Logger.LogInformation("Refreshing movie {MovieName}. (Movie={MovieId},Episode={EpisodeId},Season={SeasonId},ExtraSeasons={ExtraIds})", movie.Name, movie.Id, episodeInfo.Id, seasonInfo.Id, seasonInfo.ExtraIds);
                await MetadataRefreshService.RefreshMovie(movie, refreshFieldsMask).ConfigureAwait(false);
                updateCount++;
            }
        }
        return updateCount;
    }

    #endregion
}