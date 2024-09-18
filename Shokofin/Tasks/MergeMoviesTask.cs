using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Shokofin.MergeVersions;
using Shokofin.Utils;

namespace Shokofin.Tasks;

public class MergeMoviesTask : IScheduledTask, IConfigurableScheduledTask
{
    /// <inheritdoc />
    public string Name => "Merge Movies";

    /// <inheritdoc />
    public string Description => "Merge all movie entries with the same Shoko Episode ID set.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoMergeMovies";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    private readonly MergeVersionsManager VersionsManager;

    private readonly LibraryScanWatcher LibraryScanWatcher;

    public MergeMoviesTask(MergeVersionsManager userSyncManager, LibraryScanWatcher libraryScanWatcher)
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
            await VersionsManager.MergeAllMovies(progress, cancellationToken);
        }
    }
}
