using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Plugins;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Models;
using Shokofin.Configuration;
using Shokofin.ExternalIds;
using Shokofin.Resolvers;
using Shokofin.SignalR.Interfaces;
using Shokofin.SignalR.Models;

using File = System.IO.File;

namespace Shokofin.SignalR;

public class SignalRConnectionManager : IDisposable
{
    private static ComponentVersion? ServerVersion =>
        Plugin.Instance.Configuration.ServerVersion;

    private static readonly DateTime EventChangedDate = DateTime.Parse("2024-04-01T04:04:00.000Z");

    private static bool UseOlderEvents =>
        ServerVersion != null && ((ServerVersion.ReleaseChannel == ReleaseChannel.Stable && ServerVersion.Version == "4.2.2.0") || (ServerVersion.ReleaseDate.HasValue && ServerVersion.ReleaseDate.Value < EventChangedDate));

    private const string HubUrl = "/signalr/aggregate?feeds=shoko";

    private static readonly TimeSpan DetectChangesThreshold = TimeSpan.FromSeconds(5);

    private readonly ILogger<SignalRConnectionManager> Logger;

    private readonly ShokoAPIClient ApiClient;

    private readonly ShokoAPIManager ApiManager;

    private readonly ShokoResolveManager ResolveManager;

    private readonly ILibraryManager LibraryManager;

    private readonly ILibraryMonitor LibraryMonitor;

    private readonly IFileSystem FileSystem;

    private HubConnection? Connection = null;

    private readonly Timer ChangesDetectionTimer;

    private string CachedKey = string.Empty;

    private readonly Dictionary<string, (DateTime LastUpdated, List<IMetadataUpdatedEventArgs> List)> ChangesPerSeries = new();

    private readonly Dictionary<int, (DateTime LastUpdated, List<(UpdateReason Reason, int ImportFolderId, string Path, IFileEventArgs Event)> List)> ChangesPerFile = new();

    public bool IsUsable => CanConnect(Plugin.Instance.Configuration);

    public bool IsActive => Connection != null;

    public HubConnectionState State => Connection == null ? HubConnectionState.Disconnected : Connection.State;

    public SignalRConnectionManager(ILogger<SignalRConnectionManager> logger, ShokoAPIClient apiClient, ShokoAPIManager apiManager, ShokoResolveManager resolveManager, ILibraryManager libraryManager, ILibraryMonitor libraryMonitor, IFileSystem fileSystem)
    {
        Logger = logger;
        ApiClient = apiClient;
        ApiManager = apiManager;
        ResolveManager = resolveManager;
        LibraryManager = libraryManager;
        LibraryMonitor = libraryMonitor;
        FileSystem = fileSystem;
        ChangesDetectionTimer = new() { AutoReset = true, Interval = TimeSpan.FromSeconds(4).TotalMilliseconds };
        ChangesDetectionTimer.Elapsed += OnIntervalElapsed;
    }

    public void Dispose()
    {
        Plugin.Instance.ConfigurationChanged -= OnConfigurationChanged;
        Disconnect();
        ChangesDetectionTimer.Elapsed -= OnIntervalElapsed;
    }

    #region Connection

    private async Task ConnectAsync(PluginConfiguration config)
    {
        if (Connection != null || !CanConnect(config))
            return;

        var builder = new HubConnectionBuilder()
            .WithUrl(config.Url + HubUrl, connectionOptions =>
                connectionOptions.AccessTokenProvider = () => Task.FromResult<string?>(config.ApiKey)
            )
            .AddJsonProtocol();

        if (config.SignalR_AutoReconnectInSeconds.Length > 0)
            builder = builder.WithAutomaticReconnect(config.SignalR_AutoReconnectInSeconds.Select(seconds => TimeSpan.FromSeconds(seconds)).ToArray());

        var connection = Connection = builder.Build();

        connection.Closed += OnDisconnected;
        connection.Reconnecting += OnReconnecting;
        connection.Reconnected += OnReconnected;

        // Attach refresh events.
        connection.On<EpisodeInfoUpdatedEventArgs>("ShokoEvent:EpisodeUpdated", OnInfoUpdated);
        connection.On<SeriesInfoUpdatedEventArgs>("ShokoEvent:SeriesUpdated", OnInfoUpdated);

        // Attach file events.
        connection.On<FileEventArgs>("ShokoEvent:FileMatched", OnFileMatched);
        connection.On<FileEventArgs>("ShokoEvent:FileDeleted", OnFileDeleted);
        if (UseOlderEvents) {
            connection.On<FileMovedEventArgs.V0>("ShokoEvent:FileMoved", OnFileRelocated);
            connection.On<FileRenamedEventArgs.V0>("ShokoEvent:FileRenamed", OnFileRelocated);
        }
        else {
            connection.On<FileMovedEventArgs>("ShokoEvent:FileMoved", OnFileRelocated);
            connection.On<FileRenamedEventArgs>("ShokoEvent:FileRenamed", OnFileRelocated);
        }

        ChangesDetectionTimer.Start();
        try {
            await connection.StartAsync().ConfigureAwait(false);

            Logger.LogInformation("Connected to Shoko Server.");
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Unable to connect to Shoko Server at this time. Please reconnect manually.");
            await DisconnectAsync().ConfigureAwait(false);
        }
    }

    private Task OnReconnected(string? connectionId)
    {
        Logger.LogInformation("Reconnected to Shoko Server. (Connection={ConnectionId})", connectionId);
        return Task.CompletedTask;
    }

    private Task OnReconnecting(Exception? exception)
    {
        Logger.LogWarning(exception, "Disconnected from Shoko Server. Attempting to reconnect…");
        return Task.CompletedTask;
    }

    private Task OnDisconnected(Exception? exception)
    {
        // Gracefull disconnection.
        if (exception == null)
            Logger.LogInformation("Gracefully disconnected from Shoko Server.");
        else
            Logger.LogWarning(exception, "Abruptly disconnected from Shoko Server.");
        return Task.CompletedTask;
    }

    public void Disconnect()
        => DisconnectAsync().ConfigureAwait(false).GetAwaiter().GetResult();

    public async Task DisconnectAsync()
    {
        if (Connection == null)
            return;

        var connection = Connection;
        Connection = null;

        if (connection.State != HubConnectionState.Disconnected)
            await connection.StopAsync();

        await connection.DisposeAsync();

        ChangesDetectionTimer.Stop();
        if (ChangesPerFile.Count > 0)
            ClearFileEvents();
        if (ChangesPerSeries.Count > 0)
            ClearAnimeEvents();
    }

    public Task ResetConnectionAsync()
        => ResetConnectionAsync(Plugin.Instance.Configuration, true);

    private void ResetConnection(PluginConfiguration config, bool shouldConnect)
        => ResetConnectionAsync(config, shouldConnect).ConfigureAwait(false).GetAwaiter().GetResult();

    private async Task ResetConnectionAsync(PluginConfiguration config, bool shouldConnect)
    {
        await DisconnectAsync();
        if (shouldConnect)
            await ConnectAsync(config);
    }

    public async Task RunAsync()
    {
        var config = Plugin.Instance.Configuration;
        CachedKey = ConstructKey(config);
        Plugin.Instance.ConfigurationChanged += OnConfigurationChanged;

        await ResetConnectionAsync(config, config.SignalR_AutoConnectEnabled);
    }

    private void OnConfigurationChanged(object? sender, BasePluginConfiguration baseConfig)
    {
        if (baseConfig is not PluginConfiguration config)
            return;
        var currentKey = ConstructKey(config);
        if (!string.Equals(currentKey, CachedKey))
        {
            Logger.LogDebug("Detected change in SignalR configuration! (Config={Config})", currentKey);
            CachedKey = currentKey;
            ResetConnection(config, Connection != null);
        }
    }

    private static bool CanConnect(PluginConfiguration config)
        => !string.IsNullOrEmpty(config.Url) && !string.IsNullOrEmpty(config.ApiKey) && config.ServerVersion != null;

    private static string ConstructKey(PluginConfiguration config)
        => $"CanConnect={CanConnect(config)},AutoReconnect={config.SignalR_AutoReconnectInSeconds.Select(s => s.ToString()).Join(',')}";

    #endregion

    #region Events
    
    #region Intervals

    private void OnIntervalElapsed(object? sender, ElapsedEventArgs eventArgs)
    {
        var filesToProcess = new List<(int, List<(UpdateReason Reason, int ImportFolderId, string Path, IFileEventArgs Event)>)>();
        var seriesToProcess = new List<(string, List<IMetadataUpdatedEventArgs>)>();
        lock (ChangesPerFile) {
            if (ChangesPerFile.Count > 0) {
                var now = DateTime.Now;
                foreach (var (fileId, (lastUpdated, list)) in ChangesPerFile) {
                    if (now - lastUpdated < DetectChangesThreshold)
                        continue;
                    filesToProcess.Add((fileId, list));
                }
                foreach (var (fileId, _) in filesToProcess)
                    ChangesPerFile.Remove(fileId);
            }
        }
        lock (ChangesPerSeries) {
            if (ChangesPerSeries.Count > 0) {
                var now = DateTime.Now;
                foreach (var (metadataId, (lastUpdated, list)) in ChangesPerSeries) {
                    if (now - lastUpdated < DetectChangesThreshold)
                        continue;
                    seriesToProcess.Add((metadataId, list));
                }
                foreach (var (metadataId, _) in seriesToProcess)
                    ChangesPerSeries.Remove(metadataId);
            }
        }
        foreach (var (fileId, changes) in filesToProcess)
            Task.Run(() => ProcessFileChanges(fileId, changes));
        foreach (var (metadataId, changes) in seriesToProcess)
            Task.Run(() => ProcessSeriesChanges(metadataId, changes));
    }

    private void ClearFileEvents()
    {
        var filesToProcess = new List<(int, List<(UpdateReason Reason, int ImportFolderId, string Path, IFileEventArgs Event)>)>();
        lock (ChangesPerFile) {
            foreach (var (fileId, (lastUpdated, list)) in ChangesPerFile) {
                filesToProcess.Add((fileId, list));
            }
            ChangesPerFile.Clear();
        }
        foreach (var (fileId, changes) in filesToProcess)
            Task.Run(() => ProcessFileChanges(fileId, changes));
    }

    private void ClearAnimeEvents()
    {
        var seriesToProcess = new List<(string, List<IMetadataUpdatedEventArgs>)>();
        lock (ChangesPerSeries) {
            foreach (var (metadataId, (lastUpdated, list)) in ChangesPerSeries) {
                seriesToProcess.Add((metadataId, list));
            }
            ChangesPerSeries.Clear();
        }
        foreach (var (metadataId, changes) in seriesToProcess)
            Task.Run(() => ProcessSeriesChanges(metadataId, changes));
    }

    #endregion

    #region File Events

    private void OnFileMatched(IFileEventArgs eventArgs)
    {
        Logger.LogDebug(
            "File matched; {ImportFolderId} {Path} (File={FileId},Location={LocationId},CrossReferences={HasCrossReferences})",
            eventArgs.ImportFolderId,
            eventArgs.RelativePath,
            eventArgs.FileId,
            eventArgs.FileLocationId,
            eventArgs.HasCrossReferences
        );

        AddFileEvent(eventArgs.FileId, UpdateReason.Updated, eventArgs.ImportFolderId, eventArgs.RelativePath, eventArgs);
    }

    private void OnFileRelocated(IFileRelocationEventArgs eventArgs)
    {
        Logger.LogDebug(
            "File relocated; {ImportFolderIdA} {PathA} → {ImportFolderIdB} {PathB} (File={FileId},Location={LocationId},CrossReferences={HasCrossReferences})",
            eventArgs.PreviousImportFolderId,
            eventArgs.PreviousRelativePath,
            eventArgs.ImportFolderId,
            eventArgs.RelativePath,
            eventArgs.FileId,
            eventArgs.FileLocationId,
            eventArgs.HasCrossReferences
        );

        AddFileEvent(eventArgs.FileId, UpdateReason.Removed, eventArgs.PreviousImportFolderId, eventArgs.PreviousRelativePath, eventArgs);
        AddFileEvent(eventArgs.FileId, UpdateReason.Added, eventArgs.ImportFolderId, eventArgs.RelativePath, eventArgs);
    }

    private void OnFileDeleted(IFileEventArgs eventArgs)
    {
        Logger.LogDebug(
            "File deleted; {ImportFolderIdB} {PathB} (File={FileId},Location={LocationId},CrossReferences={HasCrossReferences})",
            eventArgs.ImportFolderId,
            eventArgs.RelativePath,
            eventArgs.FileId,
            eventArgs.FileLocationId,
            eventArgs.HasCrossReferences
        );

        AddFileEvent(eventArgs.FileId, UpdateReason.Removed, eventArgs.ImportFolderId, eventArgs.RelativePath, eventArgs);
    }

    private void AddFileEvent(int fileId, UpdateReason reason, int importFolderId, string filePath, IFileEventArgs eventArgs)
    {
        lock (ChangesPerFile) {
            if (ChangesPerFile.TryGetValue(fileId, out var tuple))
                tuple.LastUpdated = DateTime.Now;
            else
                ChangesPerFile.Add(fileId, tuple = (DateTime.Now, new()));
            tuple.List.Add((reason, importFolderId, filePath, eventArgs));
        }
    }

    private async Task ProcessFileChanges(int fileId, List<(UpdateReason Reason, int ImportFolderId, string Path, IFileEventArgs Event)> changes)
    {
        try {
            Logger.LogInformation("Processing {EventCount} file change events… (File={FileId})", changes.Count, fileId);

            // Something was added or updated.
            var locationsToNotify = new List<string>();
            var seriesIds = await GetSeriesIdsForFile(fileId, changes.Select(t => t.Event).LastOrDefault(e => e.HasCrossReferences));
            var mediaFolders = ResolveManager.GetAvailableMediaFolders(fileEvents: true);
            var (reason, importFolderId, relativePath, lastEvent) = changes.Last();
            if (reason != UpdateReason.Removed) {
                foreach (var (config, mediaFolder, vfsPath) in mediaFolders) {
                    if (config.ImportFolderId != importFolderId || !config.IsEnabledForPath(relativePath))
                        continue;

                    var sourceLocation = Path.Join(mediaFolder.Path, relativePath[config.ImportFolderRelativePath.Length..]);
                    if (!File.Exists(sourceLocation))
                        continue;

                    // Let the core logic handle the rest.
                    if (!config.IsVirtualFileSystemEnabled) {
                        locationsToNotify.Add(sourceLocation);
                        continue;
                    }

                    var result = new LinkGenerationResult();
                    var topFolders = new HashSet<string>();
                    var vfsLocations = (await Task.WhenAll(seriesIds.Select(seriesId => ResolveManager.GenerateLocationsForFile(mediaFolder, sourceLocation, fileId.ToString(), seriesId))).ConfigureAwait(false))
                        .Where(tuple => !string.IsNullOrEmpty(tuple.sourceLocation) && tuple.importedAt.HasValue)
                        .ToList();
                    foreach (var (srcLoc, symLnks, nfoFls, imprtDt) in vfsLocations) {
                        result += ResolveManager.GenerateSymbolicLinks(srcLoc, symLnks, nfoFls, imprtDt!.Value, result.Paths);
                        foreach (var path in symLnks.Select(path => Path.Join(vfsPath, path[(vfsPath.Length + 1)..].Split(Path.DirectorySeparatorChar).First())).Distinct())
                            topFolders.Add(path);
                    }

                    // Remove old links for file.
                    var videos = LibraryManager
                        .GetItemList(
                            new() {
                                AncestorIds = new[] { mediaFolder.Id },
                                IncludeItemTypes = new[] { BaseItemKind.Episode, BaseItemKind.Movie },
                                HasAnyProviderId = new Dictionary<string, string> { { ShokoFileId.Name, fileId.ToString() } },
                                DtoOptions = new(true),
                            },
                            true
                        )
                        .Where(item => !string.IsNullOrEmpty(item.Path) && item.Path.StartsWith(vfsPath) && !result.Paths.Contains(item.Path))
                        .ToList();
                    foreach (var video in videos) {
                        File.Delete(video.Path);
                        topFolders.Add(Path.Join(vfsPath, video.Path[(vfsPath.Length + 1)..].Split(Path.DirectorySeparatorChar).First()));
                        locationsToNotify.Add(video.Path);
                        result.RemovedVideos++;
                    }

                    result.Print(Logger, mediaFolder.Path);

                    // If all the "top-level-folders" exist, then let the core logic handle the rest.
                    if (topFolders.All(path => LibraryManager.FindByPath(path, true) != null)) {
                        var old = locationsToNotify.Count;
                        locationsToNotify.AddRange(vfsLocations.SelectMany(tuple => tuple.symbolicLinks));
                    }
                    // Else give the core logic _any_ file or folder placed directly in the media folder, so it will schedule the media folder to be refreshed.
                    else {
                        var fileOrFolder = FileSystem.GetFileSystemEntryPaths(mediaFolder.Path, false).FirstOrDefault();
                        if (!string.IsNullOrEmpty(fileOrFolder))
                            locationsToNotify.Add(fileOrFolder);
                    }
                }
            }
            // Something was removed, so assume the location is gone.
            else if (changes.FirstOrDefault(t => t.Reason == UpdateReason.Removed).Event is IFileRelocationEventArgs firstRemovedEvent) {
                relativePath = firstRemovedEvent.RelativePath;
                importFolderId = firstRemovedEvent.ImportFolderId;
                foreach (var (config, mediaFolder, vfsPath) in mediaFolders) {
                    if (config.ImportFolderId != importFolderId || !config.IsEnabledForPath(relativePath))
                        continue;


                    // Let the core logic handle the rest.
                    if (!config.IsVirtualFileSystemEnabled) {
                        var sourceLocation = Path.Join(mediaFolder.Path, relativePath[config.ImportFolderRelativePath.Length..]);
                        locationsToNotify.Add(sourceLocation);
                        continue;
                    }

                    // Check if we can use another location for the file.
                    var result = new LinkGenerationResult();
                    var vfsSymbolicLinks = new HashSet<string>();
                    var topFolders = new HashSet<string>();
                    var newRelativePath = await GetNewRelativePath(config, fileId, relativePath);
                    if (!string.IsNullOrEmpty(newRelativePath)) {
                        var newSourceLocation = Path.Join(mediaFolder.Path, newRelativePath[config.ImportFolderRelativePath.Length..]);
                        var vfsLocations = (await Task.WhenAll(seriesIds.Select(seriesId => ResolveManager.GenerateLocationsForFile(mediaFolder, newSourceLocation, fileId.ToString(), seriesId))).ConfigureAwait(false))
                            .Where(tuple => !string.IsNullOrEmpty(tuple.sourceLocation) && tuple.importedAt.HasValue)
                            .ToList();
                        foreach (var (srcLoc, symLnks, nfoFls, imprtDt) in vfsLocations) {
                            result += ResolveManager.GenerateSymbolicLinks(srcLoc, symLnks, nfoFls, imprtDt!.Value, result.Paths);
                            foreach (var path in symLnks.Select(path => Path.Join(vfsPath, path[(vfsPath.Length + 1)..].Split(Path.DirectorySeparatorChar).First())).Distinct())
                                topFolders.Add(path);
                        }
                        vfsSymbolicLinks = vfsLocations.Select(tuple => tuple.sourceLocation).ToHashSet();
                    }

                    // Remove old links for file.
                    var videos = LibraryManager
                        .GetItemList(
                            new() {
                                AncestorIds = new[] { mediaFolder.Id },
                                IncludeItemTypes = new[] { BaseItemKind.Episode, BaseItemKind.Movie },
                                HasAnyProviderId = new Dictionary<string, string> { { ShokoFileId.Name, fileId.ToString() } },
                                DtoOptions = new(true),
                            },
                            true
                        )
                        .Where(item => !string.IsNullOrEmpty(item.Path) && item.Path.StartsWith(vfsPath) && !result.Paths.Contains(item.Path))
                        .ToList();
                    foreach (var video in videos) {
                        File.Delete(video.Path);
                        topFolders.Add(Path.Join(vfsPath, video.Path[(vfsPath.Length + 1)..].Split(Path.DirectorySeparatorChar).First()));
                        locationsToNotify.Add(video.Path);
                        result.RemovedVideos++;
                    }

                    result.Print(Logger, mediaFolder.Path);

                    // If all the "top-level-folders" exist, then let the core logic handle the rest.
                    if (topFolders.All(path => LibraryManager.FindByPath(path, true) != null)) {
                        var old = locationsToNotify.Count;
                        locationsToNotify.AddRange(vfsSymbolicLinks);
                    }
                    // Else give the core logic _any_ file or folder placed directly in the media folder, so it will schedule the media folder to be refreshed.
                    else {
                        var fileOrFolder = FileSystem.GetFileSystemEntryPaths(mediaFolder.Path, false).FirstOrDefault();
                        if (!string.IsNullOrEmpty(fileOrFolder))
                            locationsToNotify.Add(fileOrFolder);
                    }
                }
            }

            // We let jellyfin take it from here.
            Logger.LogDebug("Notifying Jellyfin about {LocationCount} changes. (File={FileId})", locationsToNotify.Count, fileId.ToString());
            foreach (var location in locationsToNotify)
                LibraryMonitor.ReportFileSystemChanged(location);
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Error processing {EventCount} file change events. (File={FileId})", changes.Count, fileId);
        }
    }

    private async Task<IReadOnlySet<string>> GetSeriesIdsForFile(int fileId, IFileEventArgs? fileEvent)
    {
        var seriesIds = fileEvent != null
            ? fileEvent.CrossReferences.Select(xref => xref.SeriesId.ToString()).Distinct().ToHashSet()
            : (await ApiClient.GetFile(fileId.ToString())).CrossReferences.Select(xref => xref.Series.Shoko.ToString()).Distinct().ToHashSet();

        var filteredSeriesIds = new HashSet<string>();
        foreach (var seriesId in seriesIds) {
            var seriesPathSet = await ApiManager.GetPathSetForSeries(seriesId);
            if (seriesPathSet.Count > 0) {
                filteredSeriesIds.Add(seriesId);
            }
        }

        // Return all series if we only have this file for all of them,
        // otherwise return only the series were we have other files that are
        // not linked to other series.
        return filteredSeriesIds.Count == 0 ? seriesIds : filteredSeriesIds;
    }

    private async Task<string?> GetNewRelativePath(MediaFolderConfiguration config, int fileId, string relativePath)
    {
        // Check if the file still exists, and if it has any other locations we can use.
        try {
            var file = await ApiClient.GetFile(fileId.ToString());
            var usableLocation = file.Locations
                .Where(loc => loc.ImportFolderId == config.ImportFolderId && config.IsEnabledForPath(loc.RelativePath) && loc.RelativePath != relativePath)
                .FirstOrDefault();
            return usableLocation?.RelativePath;
        }
        catch (ApiException ex) {
            if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            throw;
        }
    }

    #endregion

    #region Refresh Events

    private void OnInfoUpdated(IMetadataUpdatedEventArgs eventArgs)
    {
        Logger.LogDebug(
            "{ProviderName} {MetadataType} {ProviderId} ({ProviderParentId}) dispatched event with {UpdateReason}. (Episode={EpisodeId},Series={SeriesId},Group={GroupId})",
            eventArgs.ProviderName,
            eventArgs.Type,
            eventArgs.ProviderId,
            eventArgs.ProviderParentId,
            eventArgs.Reason,
            eventArgs.EpisodeIds,
            eventArgs.SeriesIds,
            eventArgs.GroupIds
        );

        if (eventArgs.Type is BaseItemKind.Episode or BaseItemKind.Series)
            AddSeriesEvent(eventArgs.ProviderParentUId ?? eventArgs.ProviderUId, eventArgs);
    }

    private void AddSeriesEvent(string metadataId, IMetadataUpdatedEventArgs eventArgs)
    {
        lock (ChangesPerSeries) {
            if (ChangesPerSeries.TryGetValue(metadataId, out var tuple))
                tuple.LastUpdated = DateTime.Now;
            else
                ChangesPerSeries.Add(metadataId, tuple = (DateTime.Now, new()));
            tuple.List.Add(eventArgs);
        }
    }

    private async Task ProcessSeriesChanges(string metadataId, List<IMetadataUpdatedEventArgs> changes)
    {
        try {
            Logger.LogInformation("Processing {EventCount} metadata change events… (Metadata={ProviderUniqueId})", changes.Count, metadataId);
            
            // Refresh all epoisodes and movies linked to the episode.

            // look up the series/season/movie, then check the media folder they're
            // in to check if the refresh event is enabled for the media folder, and
            // only send out the events if it's enabled.

            // Refresh the show and all entries beneath it, or all movies linked to
            // the show.
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Error processing {EventCount} metadata change events. (Metadata={ProviderUniqueId})", changes.Count, metadataId);
            
        }
    }

    #endregion

    #endregion
}