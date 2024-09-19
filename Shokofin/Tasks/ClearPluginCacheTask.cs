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
public class ClearPluginCacheTask(ShokoAPIManager apiManager, ShokoAPIClient apiClient, VirtualFileSystemService vfsService) : IScheduledTask, IConfigurableScheduledTask
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
    public bool IsHidden => !Plugin.Instance.Configuration.ExpertMode;

    /// <inheritdoc />
    public bool IsEnabled => Plugin.Instance.Configuration.ExpertMode;

    /// <inheritdoc />
    public bool IsLogged => true;

    private readonly ShokoAPIManager _apiManager = apiManager;

    private readonly ShokoAPIClient _apiClient = apiClient;

    private readonly VirtualFileSystemService _vfsService = vfsService;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [];

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _apiClient.Clear();
        _apiManager.Clear();
        _vfsService.Clear();
        return Task.CompletedTask;
    }
}
