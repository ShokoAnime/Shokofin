using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Shokofin.MergeVersions;
using Shokofin.Utils;

namespace Shokofin.Tasks;

/// <summary
public class SplitEpisodesTask : IScheduledTask, IConfigurableScheduledTask
{
    /// <inheritdoc />
    public string Name => "Split Episodes";

    /// <inheritdoc />
    public string Description => "Split all episode entries with a Shoko Episode ID set.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoSplitEpisodes";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public bool IsLogged => true;

    private readonly MergeVersionsManager VersionsManager;

    private readonly LibraryScanWatcher LibraryScanWatcher;

    public SplitEpisodesTask(MergeVersionsManager userSyncManager, LibraryScanWatcher libraryScanWatcher)
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
            await VersionsManager.SplitAllEpisodes(progress, cancellationToken);
        }
    }
}
