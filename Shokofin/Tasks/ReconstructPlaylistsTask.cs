using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Shokofin.Playlists;

namespace Shokofin.Tasks;

/// <summary>
/// Reconstruct all Shoko playlists outside a Library Scan. For debugging and troubleshooting. DO NOT MANUALLY RUN THIS TASK WHILE A LIBRARY SCAN IS RUNNING.
/// </summary>
public class ReconstructPlaylistsTask(PlaylistManager _playlistManager) : IScheduledTask, IConfigurableScheduledTask {
    /// <inheritdoc />
    public string Name => "Reconstruct Playlists";

    /// <inheritdoc />
    public string Description => "Reconstruct all Shoko playlists outside a Library Scan. For debugging and troubleshooting. DO NOT MANUALLY RUN THIS TASK WHILE A LIBRARY SCAN IS RUNNING.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoReconstructPlaylists";

    /// <inheritdoc />
    public bool IsHidden => !Plugin.Instance.Configuration.AdvancedMode;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [];

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) {
        using (Plugin.Instance.Tracker.Enter("Reconstruct Playlists Task")) {
            await _playlistManager.ReconstructPlaylists(progress, cancellationToken);
        }
    }
}
