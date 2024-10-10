using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Shokofin.MergeVersions;
using Shokofin.Utils;

namespace Shokofin.Tasks;

/// <summary>
/// Merge all episode entries with the same Shoko Episode ID set. For debugging and troubleshooting. DO NOT RUN THIS TASK WHILE A LIBRARY SCAN IS RUNNING.
/// </summary>
public class MergeEpisodesTask(MergeVersionsManager userSyncManager, LibraryScanWatcher libraryScanWatcher) : IScheduledTask, IConfigurableScheduledTask
{
    private readonly MergeVersionsManager _mergeVersionManager = userSyncManager;

    private readonly LibraryScanWatcher _libraryScanWatcher = libraryScanWatcher;

    /// <inheritdoc />
    public string Name => "Merge Episodes";

    /// <inheritdoc />
    public string Description => "Merge all episode entries with the same Shoko Episode ID set. For debugging and troubleshooting. DO NOT RUN THIS TASK WHILE A LIBRARY SCAN IS RUNNING.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoMergeEpisodes";

    /// <inheritdoc />
    public bool IsHidden => !Plugin.Instance.Configuration.ExpertMode;

    /// <inheritdoc />
    public bool IsEnabled => Plugin.Instance.Configuration.ExpertMode;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [];

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (_libraryScanWatcher.IsScanRunning)
            return;

        using (Plugin.Instance.Tracker.Enter("Merge Episodes Task")) {
            await _mergeVersionManager.SplitAndMergeAllEpisodes(progress, cancellationToken);
        }
    }
}
