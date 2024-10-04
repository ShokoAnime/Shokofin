using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Shokofin.Utils;

namespace Shokofin.Tasks;

/// <summary>
/// Clean-up any old VFS roots leftover from an outdated install or failed removal of the roots.
/// </summary>
public class CleanupVirtualRootTask(ILogger<CleanupVirtualRootTask> logger, IFileSystem fileSystem, LibraryScanWatcher scanWatcher) : IScheduledTask, IConfigurableScheduledTask
{
    /// <inheritdoc />
    public string Name => "Clean-up Virtual File System Roots";

    /// <inheritdoc />
    public string Description => "Clean-up any old VFS roots leftover from an outdated install or failed removal of the roots.";

    /// <inheritdoc />
    public string Category => "Shokofin";

    /// <inheritdoc />
    public string Key => "ShokoCleanupVirtualRoot";

    /// <inheritdoc />
    public bool IsHidden => !Plugin.Instance.Configuration.ExpertMode;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => Plugin.Instance.Configuration.ExpertMode;

    private readonly ILogger<CleanupVirtualRootTask> Logger = logger;

    private readonly IFileSystem FileSystem = fileSystem;

    private readonly LibraryScanWatcher ScanWatcher = scanWatcher;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [
            new() {
                Type = TaskTriggerInfo.TriggerStartup,
            },
        ];

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (ScanWatcher.IsScanRunning)
            return Task.CompletedTask;

        var start = DateTime.Now;
        var virtualRoots = Plugin.Instance.AllVirtualRoots
            .Where(Directory.Exists)
            .ToList();
        Logger.LogDebug("Found {RemoveCount} VFS roots to remove.", virtualRoots.Count);
        foreach (var virtualRoot in virtualRoots) {
            var folderStart = DateTime.Now;
            Logger.LogTrace("Removing VFS root {Path}.", virtualRoot);
            Directory.Delete(virtualRoot, true);
            var perFolderDeltaTime = DateTime.Now - folderStart;
            Logger.LogTrace("Removed VFS root {Path} in {TimeSpan}.", virtualRoot, perFolderDeltaTime);
        }

        var mediaFolders = Plugin.Instance.Configuration.MediaFolders.ToList()
            .Select(config => config.LibraryId.ToString())
            .Distinct()
            .ToList();
        var vfsRoots = FileSystem.GetDirectories(Plugin.Instance.VirtualRoot, false)
            .ExceptBy(mediaFolders, directoryInfo => directoryInfo.Name)
            .ToList();
        Logger.LogDebug("Found {RemoveCount} VFS library roots to remove.", vfsRoots.Count);
        foreach (var vfsRoot in vfsRoots) {
            var folderStart = DateTime.Now;
            Logger.LogTrace("Removing VFS library root for {Id}.", vfsRoot.Name);
            Directory.Delete(vfsRoot.FullName, true);
            var perFolderDeltaTime = DateTime.Now - folderStart;
            Logger.LogTrace("Removed VFS library root for {Id} in {TimeSpan}.", vfsRoot.Name, perFolderDeltaTime);
        }

        var deltaTime = DateTime.Now - start;
        Logger.LogDebug("Removed {RemoveCount} VFS roots in {TimeSpan}.", vfsRoots.Count, deltaTime);

        return Task.CompletedTask;
    }
}
