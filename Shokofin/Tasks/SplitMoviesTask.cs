using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Shokofin.MergeVersions;

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
    public bool IsEnabled => false;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <summary>
    /// The merge-versions manager.
    /// </summary>
    private readonly MergeVersionsManager VersionsManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="SplitMoviesTask" /> class.
    /// </summary>
    public SplitMoviesTask(MergeVersionsManager userSyncManager)
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
        await VersionsManager.SplitAllMovies(progress, cancellationToken);
    }
}
