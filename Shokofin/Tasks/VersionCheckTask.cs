using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.API.Models;

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

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var updated = false;
        var version = await ApiClient.GetVersion();
        if (version != null && (
            Plugin.Instance.Configuration.ServerVersion == null ||
            !string.Equals(version.ToString(), Plugin.Instance.Configuration.ServerVersion.ToString())
        )) {
            Logger.LogInformation("Found new Shoko Server version; {version}", version);
            Plugin.Instance.Configuration.ServerVersion = version;
            updated = true;
        }

        var mediaFolders = Plugin.Instance.Configuration.MediaFolders;
        var importFolderNameMap = await Task
            .WhenAll(
                mediaFolders
                    .Select(m => m.ImportFolderId)
                    .Distinct()
                    .Except(new int[1] { 0 })
                    .Select(id => ApiClient.GetImportFolder(id))
                    .ToList()
            )
            .ContinueWith(task => task.Result.OfType<ImportFolder>().ToDictionary(i => i.Id, i => i.Name))
            .ConfigureAwait(false);
        foreach (var mediaFolder in mediaFolders) {
            if (!importFolderNameMap.TryGetValue(mediaFolder.ImportFolderId, out var importFolderName))
                importFolderName = null;

            if (!string.Equals(mediaFolder.ImportFolderName, importFolderName)) {
                Logger.LogInformation("Found new name for import folder; {name} (ImportFolder={ImportFolderId})", importFolderName, mediaFolder.ImportFolderId);
                mediaFolder.ImportFolderName = importFolderName;
                updated = true;
            }
        }
        if (updated) {
            Plugin.Instance.UpdateConfiguration();
        }
    }
}