using System;

namespace Shokofin.Sync;

/// <summary>
/// Determines if we should push or pull the data.
/// /// </summary>
[Flags]
public enum SyncDirection {
    /// <summary>
    /// Import data from Shoko.
    /// </summary>
    Import = 1,
    /// <summary>
    /// Export data to Shoko.
    /// </summary>
    Export = 2,
    /// <summary>
    /// Sync data with Shoko and only keep the latest data.
    /// <br/>
    /// This will conditionally import or export the data as needed.
    /// </summary>
    Sync = 3,
}
