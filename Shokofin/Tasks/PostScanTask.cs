using MediaBrowser.Controller.Library;
using Shokofin.API;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shokofin.Tasks
{
    public class PostScanTask : ILibraryPostScanTask
    {
        private ShokoAPIManager ApiManager;

        public PostScanTask(ShokoAPIManager apiManager)
        {
            ApiManager = apiManager;
        }

        public async Task Run(IProgress<double> progress, CancellationToken token)
        {
            await ApiManager.PostProcess(progress, token).ConfigureAwait(false);
        }
    }
}
