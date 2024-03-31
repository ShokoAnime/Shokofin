using System;
using System.ComponentModel.DataAnnotations;

namespace Shokofin.Configuration;

/// <summary>
/// Per user configuration.
/// </summary>
public class UserConfiguration 
{
    /// <summary>
    /// The Jellyfin user id this configuration is for.
    /// </summary>
    public Guid UserId { get; set; } = Guid.Empty;

    /// <summary>
    /// Enables watch-state synchronization for the user.
    /// </summary>
    public bool EnableSynchronization { get; set; }

    /// <summary>
    /// Enable the stop event for syncing after video playback.
    /// </summary>
    public bool SyncUserDataAfterPlayback { get; set; }

    /// <summary>
    /// Enable the play/pause/resume(/stop) events for syncing under/during
    /// video playback.
    /// </summary>
    public bool SyncUserDataUnderPlayback { get; set; }

    /// <summary>
    /// Enable the scrobble event for live syncing under/during video
    /// playback.
    /// </summary>
    public bool SyncUserDataUnderPlaybackLive { get; set; }

    /// <summary>
    /// Number of playback events to skip before starting to send the events
    /// to Shoko. This is to prevent accidentially updating user watch data
    /// when a user miss clicked on a video.
    /// </summary>
    [Range(0, 200)]
    public byte SyncUserDataInitialSkipEventCount { get; set; } = 0;

    /// <summary>
    /// Number of ticks to skip (1 tick is 10 seconds) before scrobbling to
    /// shoko.
    /// </summary>
    [Range(1, 250)]
    public byte SyncUserDataUnderPlaybackAtEveryXTicks { get; set; } = 6;

    /// <summary>
    /// Imminently scrobble if the playtime changes above this threshold
    /// given in ticks (ticks in a time-span).
    /// </summary>
    /// <value></value>
    public long SyncUserDataUnderPlaybackLiveThreshold { get; set; } = 125000000; // 12.5s

    /// <summary>
    /// Enable syncing user data when an item have been added/updated.
    /// </summary>
    public bool SyncUserDataOnImport { get; set; }

    /// <summary>
    /// Enabling user data sync. for restricted videos (H).
    /// </summary>
    public bool SyncRestrictedVideos { get; set; }

    /// <summary>
    /// The username of the linked user in Shoko.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// The API Token for authentication/authorization with Shoko Server.
    /// </summary>
    public string Token { get; set; } = string.Empty;
}
