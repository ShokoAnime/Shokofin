using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shokofin.API.Models;
using Shokofin.Configuration;
using Shokofin.Events;
using Shokofin.Events.Interfaces;
using Shokofin.SignalR.Models;
using Shokofin.Utils;

namespace Shokofin.SignalR;

public class SignalRConnectionManager
{
    private static ComponentVersion? ServerVersion =>
        Plugin.Instance.Configuration.ServerVersion;

    private static readonly DateTime EventChangedDate = DateTime.Parse("2024-04-01T04:04:00.000Z");

    private static bool UseOlderEvents =>
        ServerVersion != null && ((ServerVersion.ReleaseChannel == ReleaseChannel.Stable && ServerVersion.Version == "4.2.2.0") || (ServerVersion.ReleaseDate.HasValue && ServerVersion.ReleaseDate.Value < EventChangedDate));

    private const string HubUrl = "/signalr/aggregate?feeds=shoko";

    private readonly ILogger<SignalRConnectionManager> Logger;

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
        EventDispatchService events,
        LibraryScanWatcher libraryScanWatcher
    )
    {
        Logger = logger;
        Events = events;
        LibraryScanWatcher = libraryScanWatcher;
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

        EventSubmitterLease = Events.RegisterEventSubmitter();
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
        // Graceful disconnection.
        if (exception == null)
            Logger.LogInformation("Gracefully disconnected from Shoko Server.");
        else
            Logger.LogWarning(exception, "Abruptly disconnected from Shoko Server.");
        return Task.CompletedTask;
    }

    public async Task DisconnectAsync()
    {
        if (Connection == null)
            return;

        var connection = Connection;
        Connection = null;

        if (connection.State != HubConnectionState.Disconnected)
            await connection.StopAsync();

        await connection.DisposeAsync();

        if (EventSubmitterLease is not null) {
            EventSubmitterLease.Dispose();
            EventSubmitterLease = null;
        }
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

    public async Task StopAsync()
    {
        Plugin.Instance.ConfigurationChanged -= OnConfigurationChanged;
        await DisconnectAsync();
    }

    private void OnConfigurationChanged(object? sender, PluginConfiguration config)
    {
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

        if (LibraryScanWatcher.IsScanRunning) {
            Logger.LogTrace(
                "Library scan is running. Skipping emit of file event. (File={FileId},Location={LocationId})",
                eventArgs.FileId,
                eventArgs.FileLocationId
            );
            return;
        }

        Events.AddFileEvent(eventArgs.FileId, UpdateReason.Updated, eventArgs.ImportFolderId, eventArgs.RelativePath, eventArgs);
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

        if (LibraryScanWatcher.IsScanRunning) {
            Logger.LogTrace(
                "Library scan is running. Skipping emit of file event. (File={FileId},Location={LocationId})",
                eventArgs.FileId,
                eventArgs.FileLocationId
            );
            return;
        }

        Events.AddFileEvent(eventArgs.FileId, UpdateReason.Removed, eventArgs.PreviousImportFolderId, eventArgs.PreviousRelativePath, eventArgs);
        Events.AddFileEvent(eventArgs.FileId, UpdateReason.Added, eventArgs.ImportFolderId, eventArgs.RelativePath, eventArgs);
    }

    private void OnFileDeleted(IFileEventArgs eventArgs)
    {
        Logger.LogDebug(
            "File deleted; {ImportFolderId} {Path} (File={FileId},Location={LocationId},CrossReferences={HasCrossReferences})",
            eventArgs.ImportFolderId,
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

        Events.AddFileEvent(eventArgs.FileId, UpdateReason.Removed, eventArgs.ImportFolderId, eventArgs.RelativePath, eventArgs);
    }

    #endregion

    #region Refresh Events

    private void OnInfoUpdated(IMetadataUpdatedEventArgs eventArgs)
    {
        if (Plugin.Instance.Configuration.SignalR_EventSources.Contains(eventArgs.ProviderName)) {
            Logger.LogTrace(
                "{ProviderName} {MetadataType} {ProviderId} ({ProviderParentId}) skipped event with {UpdateReason}; provider not is not enabled in the plugin settings. (Episode={EpisodeId},Series={SeriesId},Group={GroupId})",
                eventArgs.ProviderName,
                eventArgs.Kind,
                eventArgs.ProviderId,
                eventArgs.ProviderParentId,
                eventArgs.Reason,
                eventArgs.EpisodeIds,
                eventArgs.SeriesIds,
                eventArgs.GroupIds
            );
            return;
        }

        Logger.LogDebug(
            "{ProviderName} {MetadataType} {ProviderId} ({ProviderParentId}) dispatched event with {UpdateReason}. (Episode={EpisodeId},Series={SeriesId},Group={GroupId})",
            eventArgs.ProviderName,
            eventArgs.Kind,
            eventArgs.ProviderId,
            eventArgs.ProviderParentId,
            eventArgs.Reason,
            eventArgs.EpisodeIds,
            eventArgs.SeriesIds,
            eventArgs.GroupIds
        );

        if (LibraryScanWatcher.IsScanRunning) {
            Logger.LogTrace(
                "Library scan is running. Skipping emit of refresh event. (Episode={EpisodeId},Series={SeriesId},Group={GroupId})",
                eventArgs.EpisodeIds,
                eventArgs.SeriesIds,
                eventArgs.GroupIds
            );
            return;
        }

        if (eventArgs.Kind is BaseItemKind.Episode or BaseItemKind.Series)
            Events.AddSeriesEvent(eventArgs.ProviderParentUId ?? eventArgs.ProviderUId, eventArgs);
    }

    #endregion

    #endregion
}