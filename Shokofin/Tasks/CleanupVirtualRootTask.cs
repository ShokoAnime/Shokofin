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
        var mediaFolders = Plugin.Instance.Configuration.MediaFolders.ToList()
            .Select(config => config.LibraryId.ToString())
            .Distinct()
            .ToList();
        var vfsRoots = FileSystem.GetDirectories(Plugin.Instance.VirtualRoot, false)
            .ExceptBy(mediaFolders, directoryInfo => directoryInfo.Name)
            .ToList();
        Logger.LogInformation("Found {RemoveCount} VFS roots to remove.", vfsRoots.Count);
        foreach (var vfsRoot in vfsRoots) {
            var folderStart = DateTime.Now;
            Logger.LogInformation("Removing VFS root for {Id}.", vfsRoot.Name);
            Directory.Delete(vfsRoot.FullName, true);
            var perFolderDeltaTime = DateTime.Now - folderStart;
            Logger.LogInformation("Removed VFS root for {Id} in {TimeSpan}.", vfsRoot.Name, perFolderDeltaTime);
        }

        var deltaTime = DateTime.Now - start;
        Logger.LogInformation("Removed {RemoveCount} VFS roots in {TimeSpan}.", vfsRoots.Count, deltaTime);

        return Task.CompletedTask;
    }
}
