using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Shokofin.API;
using Shokofin.Resolvers;

namespace Shokofin.Tasks;

/// <summary>
/// Forcefully clear the plugin cache. For debugging and troubleshooting. DO NOT RUN THIS TASK WHILE A LIBRARY SCAN IS RUNNING.
/// </summary>
public class ClearPluginCacheTask : IScheduledTask, IConfigurableScheduledTask
{
    /// <inheritdoc />
    public string Name => "Clear Plugin Cache";

    /// <inheritdoc />
    public string Description => "Forcefully clear the plugin cache. For debugging and troubleshooting. DO NOT RUN THIS TASK WHILE A LIBRARY SCAN IS RUNNING.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoClearPluginCache";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public bool IsLogged => true;

    private readonly ShokoAPIManager ApiManager;

    private readonly ShokoAPIClient ApiClient;

    private readonly VirtualFileSystemService VfsService;

    public ClearPluginCacheTask(ShokoAPIManager apiManager, ShokoAPIClient apiClient, VirtualFileSystemService vfsService)
    {
        ApiManager = apiManager;
        ApiClient = apiClient;
        VfsService = vfsService;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => Array.Empty<TaskTriggerInfo>();

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ApiClient.Clear();
        ApiManager.Clear();
        VfsService.Clear();
        return Task.CompletedTask;
    }
}
