using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Shokofin.MergeVersions;

namespace Shokofin.Tasks;

/// <summary>
/// Merge all queued episode entries with the same Shoko Episode ID set. 
/// </summary>
public class MergeQueuedEpisodesTask(MergeVersionsManager _mergeVersionsManager) : IScheduledTask, IConfigurableScheduledTask {
    /// <inheritdoc />
    public string Name => "Merge Queued Episodes";

    /// <inheritdoc />
    public string Description => "Merge all queued episode entries with the same Shoko Episode ID set.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoMergeQueuedEpisodes";

    /// <inheritdoc />
    public bool IsHidden => true;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [];

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) {
        using (Plugin.Instance.Tracker.Enter("Merge Queued Episodes Task")) {
            await _mergeVersionsManager.SplitAndMergeQueuedEpisodes(progress, cancellationToken);
        }
    }
}
