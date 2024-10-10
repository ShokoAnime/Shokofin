using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Shokofin.Sync;

namespace Shokofin.Tasks;

public class SyncUserDataTask(UserDataSyncManager userSyncManager) : IScheduledTask, IConfigurableScheduledTask
{
    private readonly UserDataSyncManager _userSyncManager = userSyncManager;

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
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [];

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _userSyncManager.ScanAndSync(SyncDirection.Sync, progress, cancellationToken);
    }
}
