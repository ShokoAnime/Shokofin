using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Shokofin.MergeVersions;
using Shokofin.Utils;

namespace Shokofin.Tasks;

/// <summary>
/// Split all movie entries with a Shoko Episode ID set. For debugging and troubleshooting. DO NOT RUN THIS TASK WHILE A LIBRARY SCAN IS RUNNING.
/// </summary>
public class SplitMoviesTask(MergeVersionsManager _mergeVersionsManager, LibraryScanWatcher _libraryScanWatcher) : IScheduledTask, IConfigurableScheduledTask {
    /// <inheritdoc />
    public string Name => "Split Movies";

    /// <inheritdoc />
    public string Description => "Split all movie entries with a Shoko Episode ID set. For debugging and troubleshooting. DO NOT RUN THIS TASK WHILE A LIBRARY SCAN IS RUNNING.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoSplitMovies";

    /// <inheritdoc />
    public bool IsHidden => !Plugin.Instance.Configuration.AdvancedMode;

    /// <inheritdoc />
    public bool IsEnabled => Plugin.Instance.Configuration.AdvancedMode;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [];

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) {
        if (_libraryScanWatcher.IsScanRunning)
            return;

        using (Plugin.Instance.Tracker.Enter("Merge Movies Task")) {
            await _mergeVersionsManager.SplitAllMovies(progress, cancellationToken).ConfigureAwait(false);
        }
    }
}
