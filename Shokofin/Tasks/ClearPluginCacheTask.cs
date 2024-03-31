using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Shokofin.API;
using Shokofin.Resolvers;

namespace Shokofin.Tasks;

/// <summary>
/// Class ClearPluginCacheTask.
/// </summary>
public class ClearPluginCacheTask : IScheduledTask, IConfigurableScheduledTask
{
    /// <inheritdoc />
    public string Name => "Clear Plugin Cache";

    /// <inheritdoc />
    public string Description => "For debugging and troubleshooting. DO NOT RUN THIS TASK WHILE A LIBRARY SCAN IS RUNNING. Clear the plugin cache.";

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

    private readonly ShokoResolveManager ResolveManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClearPluginCacheTask" /> class.
    /// </summary>
    public ClearPluginCacheTask(ShokoAPIManager apiManager, ShokoAPIClient apiClient, ShokoResolveManager resolveManager)
    {
        ApiManager = apiManager;
        ApiClient = apiClient;
        ResolveManager = resolveManager;
    }

    /// <summary>
    /// Creates the triggers that define when the task will run.
    /// </summary>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => Array.Empty<TaskTriggerInfo>();

    /// <summary>
    /// Returns the task to be executed.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="progress">The progress.</param>
    /// <returns>Task.</returns>
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ApiClient.Clear();
        ApiManager.Clear();
        ResolveManager.Clear();
        return Task.CompletedTask;
    }
}
