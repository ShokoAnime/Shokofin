using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;

namespace Shokofin.Tasks;

public class PostScanTask(ITaskManager taskManager) : ILibraryPostScanTask {
    /// <inheritdoc />
    public Task Run(IProgress<double> progress, CancellationToken token) {
        if (Plugin.Instance.Configuration.AutoReconstructCollections) {
            taskManager.CancelIfRunningAndQueue<ReconstructCollectionsTask>();
        }
        if (Plugin.Instance.Configuration.AutoMergeVersions) {
            taskManager.CancelIfRunningAndQueue<MergeQueuedMoviesTask>();
            taskManager.CancelIfRunningAndQueue<MergeQueuedEpisodesTask>();
        }
        return Task.CompletedTask;
    }
}
