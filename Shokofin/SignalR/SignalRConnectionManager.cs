using System;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shokofin.API.Models;
using Shokofin.Configuration;
using Shokofin.Resolvers;
using Shokofin.SignalR.Interfaces;
using Shokofin.SignalR.Models;

namespace Shokofin.SignalR;

public class SignalRConnectionManager : IDisposable
{
    private static ComponentVersion? ServerVersion =>
        Plugin.Instance.Configuration.ServerVersion;

    private static readonly DateTime EventChangedDate = DateTime.Parse("2024-04-01T04:04:00.000Z");

    private static bool UseOlderEvents =>
        ServerVersion != null && ((ServerVersion.ReleaseChannel == ReleaseChannel.Stable && ServerVersion.Version == "4.2.2.0") || (ServerVersion.ReleaseDate.HasValue && ServerVersion.ReleaseDate.Value < EventChangedDate));

    private const string HubUrl = "/signalr/aggregate?feeds=shoko";

    private readonly ILogger<SignalRConnectionManager> Logger;

    private readonly ShokoResolveManager ResolveManager;

    private readonly ILibraryManager LibraryManager;

    private readonly ILibraryMonitor LibraryMonitor;

    private HubConnection? Connection = null;

    private string CachedKey = string.Empty;

    public bool IsUsable => CanConnect(Plugin.Instance.Configuration);

    public bool IsActive => Connection != null;

    public HubConnectionState State => Connection == null ? HubConnectionState.Disconnected : Connection.State;

    public SignalRConnectionManager(ILogger<SignalRConnectionManager> logger, ShokoResolveManager resolveManager, ILibraryManager libraryManager, ILibraryMonitor libraryMonitor)
    {
        Logger = logger;
        ResolveManager = resolveManager;
        LibraryManager = libraryManager;
        LibraryMonitor = libraryMonitor;
    }

    public void Dispose()
    {
        Plugin.Instance.ConfigurationChanged -= OnConfigurationChanged;
        Disconnect();
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
        connection.On<EpisodeInfoUpdatedEventArgs>("ShokoEvent:EpisodeUpdated", OnEpisodeInfoUpdated);
        connection.On<SeriesInfoUpdatedEventArgs>("ShokoEvent:SeriesUpdated", OnSeriesInfoUpdated);

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
        if (exception == null) {
            Logger.LogInformation("Gracefully disconnected from Shoko Server.");
            
        }
        else {
            Logger.LogWarning(exception, "Abruptly disconnected from Shoko Server.");
        }
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

    #region File Events

    private void OnFileMatched(IFileEventArgs eventArgs)
    {
        Logger.LogDebug(
            "File matched; {ImportFolderId} {Path} (File={FileId})",
            eventArgs.ImportFolderId,
            eventArgs.RelativePath,
            eventArgs.FileId
        );

        // also check if the locations we've found are mapped, and if they are
        // check if the file events are enabled for the media folder before
        // emitting events for the paths within the media filder.

        // check if the file is already in a known media library, and if yes,
        // promote it from "unknown" to "known". also generate vfs entries now
        // if needed.
    }

    private void OnFileRelocated(IFileRelocationEventArgs eventArgs)
    {
        Logger.LogDebug(
            "File relocated; {ImportFolderIdA} {PathA} → {ImportFolderIdB} {PathB} (File={FileId})",
            eventArgs.PreviousImportFolderId,
            eventArgs.PreviousRelativePath,
            eventArgs.ImportFolderId,
            eventArgs.RelativePath,
            eventArgs.FileId
        );

        // check the previous and current locations, and report the changes.

        // also check if the locations we've found are mapped, and if they are
        // check if the file events are enabled for the media folder before
        // emitting events for the paths within the media filder.

        // also if the vfs is used, check the vfs for broken links, and fix it,
        // or remove the broken links. we can do this a) generating the new links
        // and/or b) checking the existing base items for their paths and checking if
        // the links broke, and if the newly generated links is not in the list provided by the base items, then remove the broken link.
    }

    private void OnFileDeleted(IFileEventArgs eventArgs)
    {
        Logger.LogDebug(
            "File deleted; {ImportFolderIdB} {PathB} (File={FileId})",
            eventArgs.ImportFolderId,
            eventArgs.RelativePath,
            eventArgs.FileId
        );
        // The location has been removed.

        // also check if the locations we've found are mapped, and if they are
        // check if the file events are enabled for the media folder before
        // emitting events for the paths within the media filder.

        // check any base items with the exact path, and any VFS entries with a
        // link leading to the exact path, or with broken links.
    }

    #endregion

    #region Refresh Events

    private void OnEpisodeInfoUpdated(EpisodeInfoUpdatedEventArgs eventArgs)
    {
        Logger.LogDebug(
            "{ProviderName} episode {ProviderId} ({ProviderSeriesId}) dispatched event {UpdateReason}. (Episode={EpisodeId},Series={SeriesId},Group={GroupId})",
            eventArgs.ProviderName,
            eventArgs.ProviderId,
            eventArgs.ProviderSeriesId,
            eventArgs.Reason,
            eventArgs.EpisodeIds,
            eventArgs.SeriesIds,
            eventArgs.GroupIds
        );

        // Refresh all epoisodes and movies linked to the episode.
    }

    private void OnSeriesInfoUpdated(SeriesInfoUpdatedEventArgs eventArgs)
    {
        Logger.LogDebug(
            "{ProviderName} series {ProviderId} dispatched event {UpdateReason}. (Series={SeriesId},Group={GroupId})",
            eventArgs.ProviderName,
            eventArgs.ProviderId,
            eventArgs.Reason,
            eventArgs.SeriesIds,
            eventArgs.GroupIds
        );

        // look up the series/season/movie, then check the media folder they're
        // in to check if the refresh event is enabled for the media folder, and
        // only send out the events if it's enabled.

        // Refresh the show and all entries beneath it, or all movies linked to
        // the show.
    }

    #endregion

    #endregion
}