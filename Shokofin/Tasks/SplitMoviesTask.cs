using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Shokofin.MergeVersions;
using Shokofin.Utils;

namespace Shokofin.Tasks;

/// <summary>
/// Class SplitMoviesTask.
/// </summary>
public class SplitMoviesTask : IScheduledTask, IConfigurableScheduledTask
{
    /// <inheritdoc />
    public string Name => "Split Movies";

    /// <inheritdoc />
    public string Description => "Split all movie entries with a Shoko Episode ID set.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoSplitMovies";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    private readonly MergeVersionsManager VersionsManager;

    private readonly LibraryScanWatcher LibraryScanWatcher;

    public SplitMoviesTask(MergeVersionsManager userSyncManager, LibraryScanWatcher libraryScanWatcher)
    {
        VersionsManager = userSyncManager;
        LibraryScanWatcher = libraryScanWatcher;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => Array.Empty<TaskTriggerInfo>();

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (LibraryScanWatcher.IsScanRunning)
            return;

        using (Plugin.Instance.Tracker.Enter("Merge Movies Task")) {
            await VersionsManager.SplitAllMovies(progress, cancellationToken);
        }
    }
}
