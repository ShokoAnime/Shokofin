using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Shokofin.Sync;

namespace Shokofin.Tasks;

public class ExportUserDataTask : IScheduledTask, IConfigurableScheduledTask
{
    /// <inheritdoc />
    public string Name => "Export User Data";

    /// <inheritdoc />
    public string Description => "Export the user-data stored in Jellyfin to Shoko. Will not import user-data from Shoko.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoExportUserData";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public bool IsLogged => true;

    private readonly UserDataSyncManager _userSyncManager;

    public ExportUserDataTask(UserDataSyncManager userSyncManager)
    {
        _userSyncManager = userSyncManager;
    }
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => Array.Empty<TaskTriggerInfo>();

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _userSyncManager.ScanAndSync(SyncDirection.Export, progress, cancellationToken);
    }
}
