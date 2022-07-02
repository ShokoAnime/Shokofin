using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Model.Globalization;
using Shokofin.Sync;
using Shokofin.API;

namespace Shokofin.Tasks
{
    /// <summary>
    /// Class ClearPluginCacheTask.
    /// </summary>
    public class ClearPluginCacheTask : IScheduledTask
    {
        /// <summary>
        /// The _library manager.
        /// </summary>
        private readonly ShokoAPIManager APIManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClearPluginCacheTask" /> class.
        /// </summary>
        public ClearPluginCacheTask(ShokoAPIManager apiManager)
        {
            APIManager = apiManager;
        }

        /// <summary>
        /// Creates the triggers that define when the task will run.
        /// </summary>
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new TaskTriggerInfo[0];
        }

        /// <summary>
        /// Returns the task to be executed.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="progress">The progress.</param>
        /// <returns>Task.</returns>
        public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            APIManager.Clear();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public string Name => "Clear Plugin Cache";

        /// <inheritdoc />
        public string Description => "For debugging and troubleshooting. DO NOT RUN THIS TASK WHILE A LIBRARY SCAN IS RUNNING. Clear the plugin cache.";

        /// <inheritdoc />
        public string Category => "Shokofin";

        /// <inheritdoc />
        public string Key => "ShokoClearPluginCache";

        /// <inheritdoc />
        public bool IsHidden => false;

        /// <inheritdoc />
        public bool IsEnabled => false;

        /// <inheritdoc />
        public bool IsLogged => true;
    }
}
