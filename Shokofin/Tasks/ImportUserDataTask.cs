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

namespace Shokofin.Tasks
{
    /// <summary>
    /// Class ImportUserDataTask.
    /// </summary>
    public class ImportUserDataTask : IScheduledTask
    {
        /// <summary>
        /// The _library manager.
        /// </summary>
        private readonly UserDataSyncManager _userSyncManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="ImportUserDataTask" /> class.
        /// </summary>
        public ImportUserDataTask(UserDataSyncManager userSyncManager)
        {
            _userSyncManager = userSyncManager;
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
        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            await _userSyncManager.ScanAndSync(SyncDirection.Import, progress, cancellationToken);
        }

        /// <inheritdoc />
        public string Name => "Import User Data";

        /// <inheritdoc />
        public string Description => "Import the user-data stored in Shoko to Jellyfin. Will not export user-data to Shoko.";

        /// <inheritdoc />
        public string Category => "Shokofin";

        /// <inheritdoc />
        public string Key => "ShokoImportUserData";

        /// <inheritdoc />
        public bool IsHidden => false;

        /// <inheritdoc />
        public bool IsEnabled => false;

        /// <inheritdoc />
        public bool IsLogged => true;
    }
}
