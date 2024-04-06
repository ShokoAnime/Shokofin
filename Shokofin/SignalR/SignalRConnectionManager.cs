using System;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shokofin.Configuration;
using Shokofin.Resolvers;
using Shokofin.SignalR.Interfaces;
using Shokofin.SignalR.Models;

namespace Shokofin.SignalR;

public class SignalRConnectionManager : IDisposable
{
    private const string HubUrl = "/signalr/aggregate?feeds=shoko";

    private readonly ILogger<SignalRConnectionManager> Logger;

    private readonly ShokoResolveManager ResolveManager;

    private readonly ILibraryManager LibraryManager;

    private readonly ILibraryMonitor LibraryMonitor;

    private HubConnection? Connection = null;

    private string LastConfigKey = string.Empty;

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

        if (config.SignalR_AutoReconnectInSeconds.Count > 0)
            builder.WithAutomaticReconnect(config.SignalR_AutoReconnectInSeconds.Select(seconds => TimeSpan.FromSeconds(seconds)).ToArray());

        var connection = Connection = builder.Build();

        connection.Closed += OnDisconnected;
        connection.Reconnecting += OnReconnecting;
        connection.Reconnected += OnReconnected;

        // Attach refresh events.
        if (config.SignalR_RefreshEnabled) {
            connection.On<FileMatchedEventArgs>("ShokoEvent:FileMatched", OnFileMatched);
            connection.On<FileMovedEventArgs>("ShokoEvent:FileMoved", OnFileMoved);
            connection.On<FileRenamedEventArgs>("ShokoEvent:FileRenamed", OnFileRenamed);
            connection.On<FileEventArgs>("ShokoEvent:FileDeleted", OnFileDeleted);
        }

        // Attach file events.
        if (config.SignalR_FileWatcherEnabled) {
            connection.On<EpisodeInfoUpdatedEventArgs>("ShokoEvent:EpisodeUpdated", OnEpisodeInfoUpdated);
            connection.On<SeriesInfoUpdatedEventArgs>("ShokoEvent:SeriesUpdated", OnSeriesInfoUpdated);
        }

        try {
            await Connection.StartAsync();

            Logger.LogInformation("Connected to Shoko Server.");
        }
        catch {
            Disconnect();
            throw;
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
        LastConfigKey = GenerateConfigKey(config);
        Plugin.Instance.ConfigurationChanged += OnConfigurationChanged;

        await ResetConnectionAsync(config, config.SignalR_AutoConnectEnabled);
    }

    private void OnConfigurationChanged(object? sender, BasePluginConfiguration baseConfig)
    {
        if (baseConfig is not PluginConfiguration config)
            return;
        var newConfigKey = GenerateConfigKey(config);
        if (!string.Equals(newConfigKey, LastConfigKey))
        {
            Logger.LogDebug("Detected change in SignalR configuration! (Config={Config})", newConfigKey);
            LastConfigKey = newConfigKey;
            ResetConnection(config, Connection != null);
        }
    }

    private static bool CanConnect(PluginConfiguration config)
        => !string.IsNullOrEmpty(config.Url) && !string.IsNullOrEmpty(config.ApiKey);

    private static string GenerateConfigKey(PluginConfiguration config)
        => $"CanConnect={CanConnect(config)},Refresh={config.SignalR_RefreshEnabled},FileWatcher={config.SignalR_FileWatcherEnabled},AutoReconnect={config.SignalR_AutoReconnectInSeconds.Select(s => s.ToString()).Join(',')}";

    #endregion

    #region Events

    #region File Events

    private void OnFileMatched(FileMatchedEventArgs eventArgs)
    {
        Logger.LogDebug(
            "File matched; {ImportFolderIdB} {PathB} (File={FileId})",
            eventArgs.ImportFolderId,
            eventArgs.RelativePath,
            eventArgs.FileId
        );

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

        // also if the vfs is used, check the vfs for broken links, and fix it,
        // or remove the broken links. we can do this a) generating the new links
        // and/or b) checking the existing base items for their paths and checking if
        // the links broke, and if the newly generated links is not in the list provided by the base items, then remove the broken link.
    }

    private void OnFileMoved(FileMovedEventArgs eventArgs)
        => OnFileRelocated(eventArgs);

    private void OnFileRenamed(FileRenamedEventArgs eventArgs)
        => OnFileRelocated(eventArgs);

    private void OnFileDeleted(FileEventArgs eventArgs)
    {
        Logger.LogDebug(
            "File deleted; {ImportFolderIdB} {PathB} (File={FileId})",
            eventArgs.ImportFolderId,
            eventArgs.RelativePath,
            eventArgs.FileId
        );
        // The location has been removed.
        // check any base items with the exact path, and any VFS entries with a
        // link leading to the exact path, or with broken links.
    }

    #endregion

    #region Refresh Events

    private void OnEpisodeInfoUpdated(EpisodeInfoUpdatedEventArgs eventArgs)
    {
        Logger.LogDebug(
            "Episode updated. (Episode={EpisodeId},Series={SeriesId},Group={GroupId})",
            eventArgs.EpisodeId,
            eventArgs.SeriesId,
            eventArgs.GroupId
        );

        // Refresh all epoisodes and movies linked to the episode.
    }

    private void OnSeriesInfoUpdated(SeriesInfoUpdatedEventArgs eventArgs)
    {
        Logger.LogDebug(
            "Series updated. (Series={SeriesId},Group={GroupId})",
            eventArgs.SeriesId,
            eventArgs.GroupId
        );

        // Refresh the show and all entries beneath it, or all movies linked to
        // the show.
    }

    #endregion

    #endregion
}