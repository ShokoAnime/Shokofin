using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Shokofin.MergeVersions;

#nullable enable
namespace Shokofin.Tasks;

/// <summary>
/// Class SplitAllTask.
/// </summary>
public class SplitAllTask : IScheduledTask, IConfigurableScheduledTask
{
    /// <inheritdoc />
    public string Name => "Split both movies and episodes";

    /// <inheritdoc />
    public string Description => "Split all movie and episode entries with a Shoko Episode ID set.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoSplitAll";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <summary>
    /// The merge-versions manager.
    /// </summary>
    private readonly MergeVersionsManager VersionsManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="SplitAllTask" /> class.
    /// </summary>
    public SplitAllTask(MergeVersionsManager userSyncManager)
    {
        VersionsManager = userSyncManager;
    }

    /// <summary>
    /// Creates the triggers that define when the task will run.
    /// </summary>
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
        await VersionsManager.SplitAll(progress, cancellationToken);
    }
}
