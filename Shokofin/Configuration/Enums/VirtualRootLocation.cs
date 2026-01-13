
namespace Shokofin.Configuration;

/// <summary>
/// The virtual root location.
/// </summary>
public enum VirtualRootLocation {
    /// <summary>
    /// Use the default virtual root location.
    /// </summary>
    Default = 0,

    // /// <summary>
    // /// Use the cache for the virtual root location.
    // /// </summary>
    // [Obsolete("Using the cache is not longer supported since Jellyfin may clear it at any moment.")]
    // Cache = 1,

    /// <summary>
    /// Use a custom virtual root location.
    /// </summary>
    Custom = 2,
}
