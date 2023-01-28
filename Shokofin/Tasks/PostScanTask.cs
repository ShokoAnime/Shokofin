using MediaBrowser.Controller.Library;
using Shokofin.API;
using Shokofin.MergeVersions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shokofin.Tasks
{
    public class PostScanTask : ILibraryPostScanTask
    {
        private readonly ShokoAPIManager ApiManager;

        private readonly MergeVersionsManager VersionsManager;

        public PostScanTask(ShokoAPIManager apiManager, MergeVersionsManager versionsManager)
        {
            ApiManager = apiManager;
            VersionsManager = versionsManager;
        }

        public async Task Run(IProgress<double> progress, CancellationToken token)
        {
            // Merge versions now if the setting is enabled.
            if (Plugin.Instance.Configuration.EXPERIMENTAL_AutoMergeVersions)
                await VersionsManager.MergeAll(progress, token);

            // Clear the cache now, since we don't need it anymore.
            ApiManager.Clear();
        }
    }
}
