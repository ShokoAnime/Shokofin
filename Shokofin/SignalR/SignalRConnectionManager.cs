using System;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Configuration;
using Shokofin.Events;
using Shokofin.Events.Interfaces;
using Shokofin.Events.Stub;
using Shokofin.Extensions;
using Shokofin.SignalR.Models;
using Shokofin.Utils;

namespace Shokofin.SignalR;

public class SignalRConnectionManager {
    private const string HubUrl = "/signalr/aggregate?feeds=shoko,metadata,file,release";

    private readonly ILogger<SignalRConnectionManager> Logger;

    private readonly ShokoApiClient ApiClient;

    private readonly EventDispatchService Events;

    private readonly LibraryScanWatcher LibraryScanWatcher;

    private IDisposable? EventSubmitterLease = null;

    private HubConnection? Connection = null;

    private string CachedKey = string.Empty;

#pragma warning disable CA1822
    public bool IsUsable => CanConnect(Plugin.Instance.Configuration);
#pragma warning restore CA1822

    public bool IsActive => Connection != null;

    public HubConnectionState State => Connection == null ? HubConnectionState.Disconnected : Connection.State;

    public SignalRConnectionManager(
        ILogger<SignalRConnectionManager> logger,
        ShokoApiClient apiClient,
        EventDispatchService events,
        LibraryScanWatcher libraryScanWatcher
    ) {
        Logger = logger;
        ApiClient = apiClient;
        Events = events;
        LibraryScanWatcher = libraryScanWatcher;
    }

    #region Connection

    private async Task ConnectAsync(PluginConfiguration config) {
        if (Connection != null || !CanConnect(config))
            return;

        var builder = new HubConnectionBuilder()
            .WithUrl(config.Url + HubUrl, connectionOptions =>
                connectionOptions.AccessTokenProvider = () => Task.FromResult<string?>(config.ApiKey)
            )
            .AddJsonProtocol();

        if (config.SignalR_AutoReconnectInSeconds.Length > 0)
            builder = builder.WithAutomaticReconnect(new SignalrRetryPolicy([.. config.SignalR_AutoReconnectInSeconds.Select(seconds => TimeSpan.FromSeconds(seconds))]));

        var connection = Connection = builder.Build();

        connection.ServerTimeout = TimeSpan.FromMinutes(1);
        connection.Closed += OnDisconnected;
        connection.Reconnecting += OnReconnecting;
        connection.Reconnected += OnReconnected;

        if (Plugin.Instance.Configuration.HasPluginsExposed) {
            // Attach metadata events.
            connection.On<EpisodeInfoUpdatedEventArgs>("metadata:episode.added", OnInfoUpdated);
            connection.On<EpisodeInfoUpdatedEventArgs>("metadata:episode.updated", OnInfoUpdated);
            connection.On<EpisodeInfoUpdatedEventArgs>("metadata:episode.removed", OnInfoUpdated);
            connection.On<SeriesInfoUpdatedEventArgs>("metadata:series.added", OnInfoUpdated);
            connection.On<SeriesInfoUpdatedEventArgs>("metadata:series.updated", OnInfoUpdated);
            connection.On<SeriesInfoUpdatedEventArgs>("metadata:series.removed", OnInfoUpdated);
            connection.On<MovieInfoUpdatedEventArgs>("metadata:movie.added", OnInfoUpdated);
            connection.On<MovieInfoUpdatedEventArgs>("metadata:movie.updated", OnInfoUpdated);
            connection.On<MovieInfoUpdatedEventArgs>("metadata:movie.removed", OnInfoUpdated);

            // Attach release events.
            connection.On<ReleaseSavedEventArgs>("release:saved", OnReleaseSaved);

            // Attach file events.
            connection.On<FileEventArgs>("file:deleted", OnFileDeleted);
            connection.On<FileMovedEventArgs>("file:relocated", OnFileRelocated);
        }
        else {
            // Attach refresh events.
            connection.On<EpisodeInfoUpdatedEventArgs>("ShokoEvent:EpisodeUpdated", OnInfoUpdated);
            connection.On<SeriesInfoUpdatedEventArgs>("ShokoEvent:SeriesUpdated", OnInfoUpdated);
            connection.On<MovieInfoUpdatedEventArgs>("ShokoEvent:MovieUpdated", OnInfoUpdated);

            // Attach file events.
            connection.On<FileEventArgs>("ShokoEvent:FileMatched", OnFileMatched);
            connection.On<FileEventArgs>("ShokoEvent:FileDeleted", OnFileDeleted);
            connection.On<FileMovedEventArgs>("ShokoEvent:FileMoved", OnFileRelocated);
            connection.On<FileRenamedEventArgs>("ShokoEvent:FileRenamed", OnFileRelocated);
        }

        EventSubmitterLease = Events.RegisterEventSubmitter();
        try {
            Logger.LogInformation("Connecting to Shoko Server.");

            await connection.StartAsync().ConfigureAwait(false);

            Logger.LogInformation("Connected to Shoko Server.");
        }
        catch (HttpRequestException ex) when (ex is { HttpRequestError: HttpRequestError.ConnectionError, InnerException: SocketException { SocketErrorCode: SocketError.ConnectionRefused } }) {
            Logger.LogWarning("Unable to connect to Shoko Server due to a connection error. Please reconnect manually.");
            await DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex) {
            Logger.LogError(ex, "An unexpected error occurred while attempting to connect to Shoko Server. Please reconnect manually.");
            await DisconnectAsync().ConfigureAwait(false);
        }
    }

    private Task OnReconnected(string? connectionId) {
        Logger.LogInformation("Reconnected to Shoko Server. (Connection={ConnectionId})", connectionId);
        return Task.CompletedTask;
    }

    private Task OnReconnecting(Exception? exception) {
        Logger.LogWarning(exception, "Disconnected from Shoko Server. Attempting to reconnect…");
        return Task.CompletedTask;
    }

    private Task OnDisconnected(Exception? exception) {
        // Graceful disconnection.
        if (exception == null)
            Logger.LogInformation("Gracefully disconnected from Shoko Server.");
        else
            Logger.LogWarning(exception, "Abruptly disconnected from Shoko Server.");
        return Task.CompletedTask;
    }

    public async Task DisconnectAsync() {
        if (Connection == null)
            return;

        var connection = Connection;
        Connection = null;

        if (connection.State != HubConnectionState.Disconnected)
            await connection.StopAsync().ConfigureAwait(false);

        await connection.DisposeAsync().ConfigureAwait(false);

        if (EventSubmitterLease is not null) {
            EventSubmitterLease.Dispose();
            EventSubmitterLease = null;
        }
    }

    public Task ResetConnectionAsync()
        => ResetConnectionAsync(Plugin.Instance.Configuration, true);

    private void ResetConnection(PluginConfiguration config, bool shouldConnect)
        => ResetConnectionAsync(config, shouldConnect).ConfigureAwait(false).GetAwaiter().GetResult();

    private async Task ResetConnectionAsync(PluginConfiguration config, bool shouldConnect) {
        await DisconnectAsync().ConfigureAwait(false);
        if (shouldConnect)
            await ConnectAsync(config).ConfigureAwait(false);
    }

    public async Task RunAsync() {
        var config = Plugin.Instance.Configuration;
        CachedKey = ConstructKey(config);
        Plugin.Instance.ConfigurationChanged += OnConfigurationChanged;

        await ResetConnectionAsync(config, config.SignalR_AutoConnectEnabled).ConfigureAwait(false);
    }

    public async Task StopAsync() {
        Plugin.Instance.ConfigurationChanged -= OnConfigurationChanged;
        await DisconnectAsync().ConfigureAwait(false);
    }

    private void OnConfigurationChanged(object? sender, PluginConfiguration config) {
        var currentKey = ConstructKey(config);
        if (!string.Equals(currentKey, CachedKey)) {
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

    #region File Events

    private void OnFileMatched(IFileEventArgs eventArgs) {
        Logger.LogDebug(
            "File matched; {ManagedFolderId} {Path} (File={FileId},Location={LocationId},CrossReferences={HasCrossReferences})",
            eventArgs.ManagedFolderId,
            eventArgs.RelativePath,
            eventArgs.FileId,
            eventArgs.FileLocationId,
            eventArgs.HasCrossReferences
        );

        if (LibraryScanWatcher.IsScanRunning) {
            Logger.LogTrace(
                "Library scan is running. Skipping emit of file event. (File={FileId},Location={LocationId})",
                eventArgs.FileId,
                eventArgs.FileLocationId
            );
            return;
        }

        Events.AddFileEvent(eventArgs.FileId, UpdateReason.MetadataUpdated, eventArgs.ManagedFolderId, eventArgs.RelativePath, eventArgs);
    }

    private async Task OnReleaseSaved(IReleaseSavedEventArgs eventArgs0) {
        if (await ApiClient.GetFile(eventArgs0.FileId.ToString()).ConfigureAwait(false) is not { } file) {
            Logger.LogDebug("File not found; {VideoId}", eventArgs0.FileId);
            return;
        }
        
        if ((file.Locations.FirstOrDefault(location => location.IsAccessible) ?? file.Locations.FirstOrDefault()) is not { } fileLocation) {
            Logger.LogDebug("File location not found; {VideoId}", eventArgs0.FileId);
            return;
        }

        var eventArgs = new FileEventArgsStub(fileLocation, file);
        Logger.LogDebug(
            "Release saved; {ManagedFolderId} {Path} (File={FileId},Location={LocationId},CrossReferences={HasCrossReferences})",
            eventArgs.ManagedFolderId,
            eventArgs.RelativePath,
            eventArgs.FileId,
            eventArgs.FileLocationId,
            eventArgs.HasCrossReferences
        );

        if (LibraryScanWatcher.IsScanRunning) {
            Logger.LogTrace(
                "Library scan is running. Skipping emit of file event. (File={FileId},Location={LocationId})",
                eventArgs.FileId,
                eventArgs.FileLocationId
            );
            return;
        }

        Events.AddFileEvent(eventArgs.FileId, UpdateReason.MetadataUpdated, eventArgs.ManagedFolderId, eventArgs.RelativePath, eventArgs);
    }

    private void OnFileRelocated(IFileRelocationEventArgs eventArgs) {
        Logger.LogDebug(
            "File relocated; {ManagedFolderIdA} {PathA} → {ManagedFolderIdB} {PathB} (File={FileId},Location={LocationId},CrossReferences={HasCrossReferences})",
            eventArgs.PreviousManagedFolderId,
            eventArgs.PreviousRelativePath,
            eventArgs.ManagedFolderId,
            eventArgs.RelativePath,
            eventArgs.FileId,
            eventArgs.FileLocationId,
            eventArgs.HasCrossReferences
        );

        if (LibraryScanWatcher.IsScanRunning) {
            Logger.LogTrace(
                "Library scan is running. Skipping emit of file event. (File={FileId},Location={LocationId})",
                eventArgs.FileId,
                eventArgs.FileLocationId
            );
            return;
        }

        Events.AddFileEvent(eventArgs.FileId, UpdateReason.MetadataRemoved, eventArgs.PreviousManagedFolderId, eventArgs.PreviousRelativePath, eventArgs);
        Events.AddFileEvent(eventArgs.FileId, UpdateReason.MetadataAdded, eventArgs.ManagedFolderId, eventArgs.RelativePath, eventArgs);
    }

    private void OnFileDeleted(IFileEventArgs eventArgs) {
        Logger.LogDebug(
            "File deleted; {ManagedFolderId} {Path} (File={FileId},Location={LocationId},CrossReferences={HasCrossReferences})",
            eventArgs.ManagedFolderId,
            eventArgs.RelativePath,
            eventArgs.FileId,
            eventArgs.FileLocationId,
            eventArgs.HasCrossReferences
        );

        if (LibraryScanWatcher.IsScanRunning) {
            Logger.LogTrace(
                "Library scan is running. Skipping emit of file event. (File={FileId},Location={LocationId})",
                eventArgs.FileId,
                eventArgs.FileLocationId
            );
            return;
        }

        Events.AddFileEvent(eventArgs.FileId, UpdateReason.MetadataRemoved, eventArgs.ManagedFolderId, eventArgs.RelativePath, eventArgs);
    }

    #endregion

    #region Refresh Events

    private void OnInfoUpdated(IMetadataUpdatedEventArgs eventArgs) {
        if (eventArgs.IsUnknownUpdate) {
            Logger.LogTrace(
                "{ProviderName} {MetadataType} {ProviderId} ({ProviderParentId}) skipped event with {UpdateReason}; no operation. (Episode={EpisodeId},Series={SeriesId})",
                eventArgs.ProviderName,
                eventArgs.Kind,
                eventArgs.ProviderId,
                eventArgs.ProviderParentId,
                eventArgs.Reason,
                eventArgs.EpisodeIds,
                eventArgs.SeriesIds
            );
            return;
        }

        if (!Plugin.Instance.Configuration.SignalR_EventSources.Contains(eventArgs.ProviderName)) {
            Logger.LogTrace(
                "{ProviderName} {MetadataType} {ProviderId} ({ProviderParentId}) skipped event with {UpdateReason}; provider is not enabled in the plugin settings. (Episode={EpisodeId},Series={SeriesId})",
                eventArgs.ProviderName,
                eventArgs.Kind,
                eventArgs.ProviderId,
                eventArgs.ProviderParentId,
                eventArgs.Reason,
                eventArgs.EpisodeIds,
                eventArgs.SeriesIds
            );
            return;
        }

        Logger.LogDebug(
            "{ProviderName} {MetadataType} {ProviderId} ({ProviderParentId}) dispatched event with {UpdateReason}. (Episode={EpisodeId},Series={SeriesId})",
            eventArgs.ProviderName,
            eventArgs.Kind,
            eventArgs.ProviderId,
            eventArgs.ProviderParentId,
            eventArgs.Reason,
            eventArgs.EpisodeIds,
            eventArgs.SeriesIds
        );

        if (LibraryScanWatcher.IsScanRunning) {
            Logger.LogTrace(
                "Library scan is running. Skipping emit of refresh event. (Episode={EpisodeId},Series={SeriesId})",
                eventArgs.EpisodeIds,
                eventArgs.SeriesIds
            );
            return;
        }

        if (eventArgs.Kind is BaseItemKind.Episode or BaseItemKind.Series or BaseItemKind.Movie)
            Events.AddSeriesEvent(eventArgs.ProviderParentUId ?? eventArgs.ProviderUId, eventArgs);
    }

    #endregion

    #endregion
}