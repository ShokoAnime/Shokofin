using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Shokofin.Collections;
using Shokofin.Utils;

namespace Shokofin.Tasks;

/// <summary>
/// Reconstruct all Shoko collections outside a Library Scan.
/// </summary>
public class ReconstructCollectionsTask : IScheduledTask, IConfigurableScheduledTask
{
    /// <inheritdoc />
    public string Name => "Reconstruct Collections";

    /// <inheritdoc />
    public string Description => "Reconstruct all Shoko collections outside a Library Scan.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoReconstructCollections";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    private readonly CollectionManager CollectionManager;

    private readonly LibraryScanWatcher LibraryScanWatcher;

    public ReconstructCollectionsTask(CollectionManager collectionManager, LibraryScanWatcher libraryScanWatcher)
    {
        CollectionManager = collectionManager;
        LibraryScanWatcher = libraryScanWatcher;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => Array.Empty<TaskTriggerInfo>();

    /// <summary>
    /// Returns the task to be executed.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="progress">The progress.</param>
    /// <returns>Task.</returns>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (LibraryScanWatcher.IsScanRunning)
            return;

        using (Plugin.Instance.Tracker.Enter("Reconstruct Collections Task")) {
            await CollectionManager.ReconstructCollections(progress, cancellationToken);
        }
    }
}
