using System;

namespace Shokofin.Configuration
{
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

        public bool SyncUserDataAfterPlayback { get; set; }
    
        public bool SyncUserDataUnderPlayback { get; set; }

        public bool SyncUserDataOnImport { get; set; }

        /// <summary>
        /// The username of the linked user in Shoko.
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// The API Token for authentication/authorization with Shoko Server.
        /// </summary>
        public string Token { get; set; } = string.Empty;
    }
}
