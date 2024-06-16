using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Shokofin.Sync;

namespace Shokofin.Tasks;

public class ImportUserDataTask : IScheduledTask, IConfigurableScheduledTask
{
    /// <inheritdoc />
    public string Name => "Import User Data";

    /// <inheritdoc />
    public string Description => "Import the user-data stored in Shoko to Jellyfin. Will not export user-data to Shoko.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoImportUserData";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public bool IsLogged => true;

    private readonly UserDataSyncManager _userSyncManager;

    public ImportUserDataTask(UserDataSyncManager userSyncManager)
    {
        _userSyncManager = userSyncManager;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => Array.Empty<TaskTriggerInfo>();

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _userSyncManager.ScanAndSync(SyncDirection.Import, progress, cancellationToken);
    }
}
