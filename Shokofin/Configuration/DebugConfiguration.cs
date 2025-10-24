using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Shokofin.Configuration;

public class DebugConfiguration {
    /// <summary>
    /// Whether or not to show the debug settings in the UI.
    /// </summary>
    public bool ShowInUI { get; set; } = false;

    /// <summary>
    /// Amount of seconds that needs to pass before the usage tracker considers
    /// the usage as stalled and resets it's tracking and dispatches it's
    /// <seealso cref="Utils.UsageTracker.Stalled"/> event.
    /// </summary>
    /// <remarks>
    /// It can be configured between 1 second and 3 hours.
    /// </remarks>
    [Range(1, 10800)]
    public int UsageTrackerStalledTimeInSeconds { get; set; } = 60;

    /// <summary>
    /// Amount of time that needs to pass before the usage tracker considers the
    /// usage as stalled and resets it's tracking and dispatches it's
    /// <seealso cref="Utils.UsageTracker.Stalled"/> event.
    /// </summary>
    [XmlIgnore, JsonIgnore]
    public TimeSpan UsageTrackerStalledTime => TimeSpan.FromSeconds(UsageTrackerStalledTimeInSeconds);

    /// <summary>
    /// Maximum number of requests of outgoing traffic at any given time.
    /// </summary>
    /// <remarks>
    /// It can be configured between 1 and 1000 requests.
    /// </remarks>
    [Range(1, 1000)]
    public int MaxInFlightRequests { get; set; } = 10;

    /// <summary>
    /// The page size to use for series queries.
    /// </summary>
    /// <remarks>
    /// It can be configured between 1 and 10,000. Set to 0 to disable pagination.
    /// </remarks>
    [Range(0, 10_000)]
    public int SeriesPageSize { get; set; } = 25;

    /// <summary>
    /// Whether or not to automatically clear the API client's cache.
    /// </summary>
    public bool AutoClearClientCache { get; set; } = true;

    /// <summary>
    /// Whether or not to automatically clear the API manager's cache.
    /// </summary>
    public bool AutoClearManagerCache { get; set; } = true;

    /// <summary>
    /// Whether or not to automatically clear the VFS' cache.
    /// </summary>
    public bool AutoClearVfsCache { get; set; } = true;

    /// <summary>
    /// The expiration scan frequency in minutes for the guarded caches.
    /// </summary>
    /// <remarks>
    /// It can be configured between 1 minute and 3 hours. Requires a restart to take effect.
    /// </remarks>
    [Range(1, 180)]
    public int ExpirationScanFrequencyInMinutes { get; set; } = 25;

    /// <summary>
    /// The expiration scan frequency.
    /// </summary>
    [XmlIgnore, JsonIgnore]
    public TimeSpan ExpirationScanFrequency => TimeSpan.FromMinutes(ExpirationScanFrequencyInMinutes);

    /// <summary>
    /// The sliding expiration in minutes for the guarded caches.
    /// </summary>
    /// <remarks>
    /// It can be configured between 1 minute and 3 hours. Requires a restart to take effect.
    /// </remarks>
    [Range(1, 180)]
    public int SlidingExpirationInMinutes { get; set; } = 15;

    /// <summary>
    /// The sliding expiration.
    /// </summary>
    [XmlIgnore, JsonIgnore]
    public TimeSpan SlidingExpiration => TimeSpan.FromMinutes(SlidingExpirationInMinutes);

    /// <summary>
    /// The absolute expiration relative to now in minutes for the guarded caches.
    /// </summary>
    /// <remarks>
    /// It can be configured between 1 minute and 24 hours. Requires a restart to take effect.
    /// </remarks>
    [Range(1, 1440)]
    public int AbsoluteExpirationRelativeToNowInMinutes { get; set; } = 120;

    /// <summary>
    /// The absolute expiration relative to now.
    /// </summary>
    [XmlIgnore, JsonIgnore]
    public TimeSpan AbsoluteExpirationRelativeToNow => TimeSpan.FromMinutes(AbsoluteExpirationRelativeToNowInMinutes);
}