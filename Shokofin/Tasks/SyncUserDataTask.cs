using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Shokofin.Sync;

#nullable enable
namespace Shokofin.Tasks;

/// <summary>
/// Class SyncUserDataTask.
/// </summary>
public class SyncUserDataTask : IScheduledTask, IConfigurableScheduledTask
{
    /// <inheritdoc />
    public string Name => "Sync User Data";

    /// <inheritdoc />
    public string Description => "Synchronize the user-data stored in Jellyfin with the user-data stored in Shoko. Imports or exports data as needed.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoSyncUserData";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <summary>
    /// The _library manager.
    /// </summary>
    private readonly UserDataSyncManager _userSyncManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncUserDataTask" /> class.
    /// </summary>
    public SyncUserDataTask(UserDataSyncManager userSyncManager)
    {
        _userSyncManager = userSyncManager;
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
        await _userSyncManager.ScanAndSync(SyncDirection.Sync, progress, cancellationToken);
    }
}
