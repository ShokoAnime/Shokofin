using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Shokofin.Events;
using Shokofin.Utils;

namespace Shokofin.Tasks;

/// <summary>
/// Automatically refresh metadata for entries managed by the plugin. DO NOT RUN THIS TASK WHILE A LIBRARY SCAN IS RUNNING.
/// </summary>
public class AutoRefreshMetadataTask(MetadataRefreshService _metadataRefreshService, LibraryScanWatcher _libraryScanWatcher) : IScheduledTask, IConfigurableScheduledTask {
    /// <inheritdoc />
    public string Name => "Auto-Refresh Metadata";

    /// <inheritdoc />
    public string Description => "Automatically refresh metadata for entries managed by the plugin. DO NOT RUN THIS TASK WHILE A LIBRARY SCAN IS RUNNING.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoAutoRefreshMetadata";

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
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) {
        if (_libraryScanWatcher.IsScanRunning)
            return;

        using (Plugin.Instance.Tracker.Enter("Auto-Refresh Metadata Task")) {
            await _metadataRefreshService.AutoRefresh(progress, cancellationToken).ConfigureAwait(false);
        }
    }
}
