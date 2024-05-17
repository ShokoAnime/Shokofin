using System;
using MediaBrowser.Controller.Library;

namespace Shokofin.Utils;

public class LibraryScanWatcher
{
    private readonly ILibraryManager LibraryManager;

    private readonly PropertyWatcher<bool> Watcher;

    public bool IsScanRunning => Watcher.Value;

    public event EventHandler<bool>? ValueChanged;

    public LibraryScanWatcher(ILibraryManager libraryManager)
    {
        LibraryManager = libraryManager;
        Watcher = new(() => LibraryManager.IsScanRunning);
        Watcher.StartMonitoring(Plugin.Instance.Configuration.LibraryScanReactionTimeInSeconds);
        Watcher.ValueChanged += OnLibraryScanRunningChanged;
    }

    ~LibraryScanWatcher()
    {
        Watcher.StopMonitoring();
        Watcher.ValueChanged -= OnLibraryScanRunningChanged;
    }

    private void OnLibraryScanRunningChanged(object? sender, bool isScanRunning)
        => ValueChanged?.Invoke(sender, isScanRunning);
}