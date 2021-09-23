using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Shokofin.API;

namespace Shokofin
{
    public class Scrobbler : IServerEntryPoint
    {
        private readonly ISessionManager SessionManager;

        private readonly ILogger<Scrobbler> Logger;

        private readonly ShokoAPIClient APIClient;

        public Scrobbler(ISessionManager sessionManager, ILogger<Scrobbler> logger, ShokoAPIClient apiClient)
        {
            SessionManager = sessionManager;
            Logger = logger;
            APIClient = apiClient;
        }

        public Task RunAsync()
        {
            SessionManager.PlaybackStopped += OnPlaybackStopped;
            return Task.CompletedTask;
        }

        private async void OnPlaybackStopped(object sender, PlaybackStopEventArgs e)
        {
            // Only sync-back if we enabled the feature in the plugin settings and an item is present
            if (!Plugin.Instance.Configuration.UpdateWatchedStatus || e.Item == null)
                return;

            // Only known episodes and movies have a file id, so if it doesn't have one then it's either urecognized or from another library.
            if (!e.Item.HasProviderId("Shoko File"))
            {
                Logger.LogWarning("Unable to find a Shoko File Id for item {ItemName}", e.Item.Name);
                return;
            }

            var fileId = e.Item.GetProviderId("Shoko File");
            var watched = e.PlayedToCompletion;
            var resumePosition = e.PlaybackPositionTicks ?? 0;
            Logger.LogInformation("Playback was stopped. Syncing watch state of file back to Shoko. (File={FileId},Watched={WatchState},ResumePosition={ResumePosition})", fileId, watched, resumePosition);
            var result = await APIClient.ScrobbleFile(fileId, watched, resumePosition);
            if (result)
                Logger.LogInformation("File marked as watched! (File={FileId})", fileId);
            else
                Logger.LogWarning("An error occured while syncing watch state of file back to Shoko! (File={FileId})", fileId);
        }

        public void Dispose()
        {
            SessionManager.PlaybackStopped -= OnPlaybackStopped;
        }
    }
}
