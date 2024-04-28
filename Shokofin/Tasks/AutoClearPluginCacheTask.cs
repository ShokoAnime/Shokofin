using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Resolvers;

namespace Shokofin.Tasks;

/// <summary>
/// For automagic maintenance. Will clear the plugin cache if there has been no recent activity to the cache.
/// </summary>
public class AutoClearPluginCacheTask : IScheduledTask, IConfigurableScheduledTask
{
    /// <inheritdoc />
    public string Name => "Clear Plugin Cache (Auto)";

    /// <inheritdoc />
    public string Description => "For automagic maintenance. Will clear the plugin cache if there has been no recent activity to the cache.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoAutoClearPluginCache";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => false;

    private readonly ILogger<AutoClearPluginCacheTask> Logger;

    private readonly ShokoAPIManager ApiManager;

    private readonly ShokoAPIClient ApiClient;

    private readonly ShokoResolveManager ResolveManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="AutoClearPluginCacheTask" /> class.
    /// </summary>
    public AutoClearPluginCacheTask(ILogger<AutoClearPluginCacheTask> logger, ShokoAPIManager apiManager, ShokoAPIClient apiClient, ShokoResolveManager resolveManager)
    {
        Logger = logger;
        ApiManager = apiManager;
        ApiClient = apiClient;
        ResolveManager = resolveManager;
    }

    /// <summary>
    /// Creates the triggers that define when the task will run.
    /// </summary>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => new TaskTriggerInfo[] {
            new() {
                Type = TaskTriggerInfo.TriggerInterval,
                IntervalTicks = TimeSpan.FromMinutes(15).Ticks,
            }
        };

    /// <summary>
    /// Returns the task to be executed.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="progress">The progress.</param>
    /// <returns>Task.</returns>
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (ApiClient.IsCacheStalled || ApiManager.IsCacheStalled || ResolveManager.IsCacheStalled)
            Logger.LogInformation("Automagically clearing cache…");
        if (ApiClient.IsCacheStalled)
            ApiClient.Clear();
        if (ApiManager.IsCacheStalled)
            ApiManager.Clear();
        if (ResolveManager.IsCacheStalled)
            ResolveManager.Clear();
        return Task.CompletedTask;
    }
}
