using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Shokofin.Sync;

namespace Shokofin.Tasks;

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

    private readonly UserDataSyncManager _userSyncManager;

    public SyncUserDataTask(UserDataSyncManager userSyncManager)
    {
        _userSyncManager = userSyncManager;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => Array.Empty<TaskTriggerInfo>();

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _userSyncManager.ScanAndSync(SyncDirection.Sync, progress, cancellationToken);
    }
}
