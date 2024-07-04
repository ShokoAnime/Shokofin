using System;
using System.Collections.Concurrent;
using System.Timers;
using Microsoft.Extensions.Logging;

namespace Shokofin.Utils;

public class UsageTracker
{
    private readonly ILogger<UsageTracker> Logger;

    private readonly object LockObj = new();

    private readonly Timer StalledTimer;

    public TimeSpan Timeout { get; private set; }

    public ConcurrentDictionary<Guid, string> CurrentTrackers { get; private init; } = new();

    public event EventHandler? Stalled;

    public UsageTracker(ILogger<UsageTracker> logger, TimeSpan timeout)
    {
        Logger = logger;
        Timeout = timeout;
        StalledTimer = new(timeout.TotalMilliseconds) {
            AutoReset = false,
            Enabled = false,
        };
        StalledTimer.Elapsed += OnTimerElapsed;
    }

    ~UsageTracker() {
        StalledTimer.Elapsed -= OnTimerElapsed;
        StalledTimer.Dispose();
    }

    public void UpdateTimeout(TimeSpan timeout)
    {
        if (Timeout == timeout)
            return;

        lock (LockObj) {
            if (Timeout == timeout)
                return;

            Logger.LogTrace("Timeout changed. (Previous={PreviousTimeout},Next={NextTimeout})", Timeout, timeout);
            var timerRunning = StalledTimer.Enabled;
            if (timerRunning)
                StalledTimer.Stop();

            Timeout = timeout;
            StalledTimer.Interval = timeout.TotalMilliseconds;

            if (timerRunning)
                StalledTimer.Start();
        }
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs eventArgs)
    {
        Logger.LogDebug("Dispatching stalled event.");
        Stalled?.Invoke(this, new());
    }

    public IDisposable Enter(string name)
    {
        var trackerId = Add(name);
        return new DisposableAction(() => Remove(trackerId));
    }

    public Guid Add(string name)
    {
        Guid trackerId = Guid.NewGuid();
        while (!CurrentTrackers.TryAdd(trackerId, name))
            trackerId = Guid.NewGuid();
        Logger.LogTrace("Added tracker to {Name}. (Id={TrackerId})", name, trackerId);
        if (StalledTimer.Enabled) {
            lock (LockObj) {
                if (StalledTimer.Enabled) {
                    Logger.LogTrace("Stopping timer.");
                    StalledTimer.Stop();
                }
            }
        }
        return trackerId;
    }

    public void Remove(Guid trackerId)
    {
        if (CurrentTrackers.TryRemove(trackerId, out var name)) {
            Logger.LogTrace("Removed tracker from {Name}. (Id={TrackerId})", name, trackerId);
            if (CurrentTrackers.IsEmpty && !StalledTimer.Enabled) {
                lock (LockObj) {
                    if (CurrentTrackers.IsEmpty && !StalledTimer.Enabled) {
                        Logger.LogTrace("Starting timer.");
                        StalledTimer.Start();
                    }
                }
            }
        }
    }
}