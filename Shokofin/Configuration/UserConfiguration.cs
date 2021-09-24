
namespace Shokofin.Configuration
{
    /// <summary>
    /// 
    /// </summary>
    public class UserConfiguration 
    {
        /// <summary>
        /// The user id for this configuration.
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether the setting is enabled for the user.
        /// </summary>
        public bool Enabled { get; set; } = false;

        public string ApiKey { get; set; } = string.Empty;

        public string Username { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;
    }
}
