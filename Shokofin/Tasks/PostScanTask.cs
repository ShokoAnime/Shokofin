using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Shokofin.API;
using Shokofin.Collections;
using Shokofin.MergeVersions;
using Shokofin.Resolvers;

namespace Shokofin.Tasks;

public class PostScanTask : ILibraryPostScanTask
{
    private readonly ShokoAPIManager ApiManager;

    private readonly ShokoAPIClient ApiClient;

    private readonly ShokoResolveManager ResolveManager;

    private readonly MergeVersionsManager VersionsManager;

    private readonly CollectionManager CollectionManager;

    public PostScanTask(ShokoAPIManager apiManager, ShokoAPIClient apiClient, ShokoResolveManager resolveManager, MergeVersionsManager versionsManager, CollectionManager collectionManager)
    {
        ApiManager = apiManager;
        ApiClient = apiClient;
        ResolveManager = resolveManager;
        VersionsManager = versionsManager;
        CollectionManager = collectionManager;
    }

    public async Task Run(IProgress<double> progress, CancellationToken token)
    {
        // Merge versions now if the setting is enabled.
        if (Plugin.Instance.Configuration.EXPERIMENTAL_AutoMergeVersions) {
            // Setup basic progress tracking
            var baseProgress = 0d;
            var simpleProgress = new Progress<double>(value => progress.Report(baseProgress + (value / 2d)));

            // Merge versions.
            await VersionsManager.MergeAll(simpleProgress, token);

            // Reconstruct collections.
            baseProgress = 50;
            await CollectionManager.ReconstructCollections(simpleProgress, token);

            progress.Report(100d);
        }
        else {
            // Reconstruct collections.
            await CollectionManager.ReconstructCollections(progress, token);
        }

        // Clear the cache now, since we don't need it anymore.
        ApiClient.Clear();
        ApiManager.Clear();
        ResolveManager.Clear();
    }
}
