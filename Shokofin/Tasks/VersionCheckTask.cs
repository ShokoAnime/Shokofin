using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.MergeVersions;

namespace Shokofin.Tasks;

/// <summary>
/// Responsible for updating the known version of the remote Shoko Server
/// instance at startup and set intervals.
/// </summary>
public class VersionCheckTask : IScheduledTask, IConfigurableScheduledTask
{
    /// <inheritdoc />
    public string Name => "Check Server Version";

    /// <inheritdoc />
    public string Description => "Responsible for updating the known version of the remote Shoko Server instance at startup and set intervals.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoVersionCheck";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    private readonly ILogger<VersionCheckTask> Logger;

    private readonly ShokoAPIClient ApiClient;

    public VersionCheckTask(ILogger<VersionCheckTask> logger, ShokoAPIClient apiClient)
    {
        Logger = logger;
        ApiClient = apiClient;
    }

    /// <summary>
    /// Creates the triggers that define when the task will run.
    /// </summary>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => new TaskTriggerInfo[2] {
            new() {
                Type = TaskTriggerInfo.TriggerStartup,
            },
            new() {
                Type = TaskTriggerInfo.TriggerDaily,
                TimeOfDayTicks = new TimeSpan(3, 0, 0).Ticks,
            },
        };

    /// <summary>
    /// Returns the task to be executed.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="progress">The progress.</param>
    /// <returns>Task.</returns>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var version = await ApiClient.GetVersion();
        if (version != null && (
            Plugin.Instance.Configuration.ServerVersion == null ||
            !string.Equals(version.ToString(), Plugin.Instance.Configuration.ServerVersion.ToString())
        )) {
            Logger.LogInformation("Found new Shoko Server version; {version}", version);
            Plugin.Instance.Configuration.ServerVersion = version;
            Plugin.Instance.SaveConfiguration();
        }
    }
}