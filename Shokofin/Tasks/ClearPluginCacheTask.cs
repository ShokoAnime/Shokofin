using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Shokofin.API;
using Shokofin.Events;
using Shokofin.MergeVersions;
using Shokofin.Resolvers;

namespace Shokofin.Tasks;

/// <summary>
/// Forcefully clear the plugin cache. For debugging and troubleshooting. DO NOT RUN THIS TASK WHILE A LIBRARY SCAN IS RUNNING.
/// </summary>
public class ClearPluginCacheTask(
    ShokoApiManager _apiManager,
    ShokoApiClient _apiClient,
    VirtualFileSystemService _vfsService,
    MergeVersionsManager _mergeVersionsManager,
    EventDispatchService _eventDispatchService
) : IScheduledTask, IConfigurableScheduledTask {
    /// <inheritdoc />
    public string Name => "Clear Plugin Cache";

    /// <inheritdoc />
    public string Description => "Forcefully clear the plugin cache. For debugging and troubleshooting. DO NOT RUN THIS TASK WHILE A LIBRARY SCAN IS RUNNING.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoClearPluginCache";

    /// <inheritdoc />
    public bool IsHidden => !Plugin.Instance.Configuration.Debug.ShowInUI;

    /// <inheritdoc />
    public bool IsEnabled => Plugin.Instance.Configuration.Debug.ShowInUI;

    /// <inheritdoc />
    public bool IsLogged => true;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [];

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) {
        _apiClient.Clear();
        _apiManager.Clear();
        _vfsService.Clear();
        _mergeVersionsManager.Clear();
        _eventDispatchService.Clear();
        return Task.CompletedTask;
    }
}
