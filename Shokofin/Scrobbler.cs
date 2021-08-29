using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
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

        public Scrobbler(ISessionManager sessionManager, ILogger<Scrobbler> logger)
        {
            SessionManager = sessionManager;
            Logger = logger;
        }

        public Task RunAsync()
        {
            SessionManager.PlaybackStopped += OnPlaybackStopped;
            return Task.CompletedTask;
        }

        private async void OnPlaybackStopped(object sender, PlaybackStopEventArgs e)
        {
            if (!Plugin.Instance.Configuration.UpdateWatchedStatus) return;

            if (e.Item == null)
            {
                Logger.LogError("Event details incomplete. Cannot process current media");
                return;
            }

            if (!e.Item.HasProviderId("Shoko Episode"))
            {
                Logger.LogError("Unrecognized file");
                return; // Skip if file does exist in Shoko
            }

            if (e.Item is Episode episode && e.PlayedToCompletion)
            {
                var episodeId = episode.GetProviderId("Shoko Episode");

                Logger.LogInformation("Item is played. Marking as watched on Shoko");
                Logger.LogInformation($"{episode.SeriesName} S{episode.Season.IndexNumber}E{episode.IndexNumber} - {episode.Name} ({episodeId})");

                var result = await ShokoAPI.MarkEpisodeWatched(episodeId);
                if (result)
                    Logger.LogInformation("Episode marked as watched!");
                else
                    Logger.LogError("Error marking episode as watched!");
            }
        }

        public void Dispose()
        {
            SessionManager.PlaybackStopped -= OnPlaybackStopped;
        }
    }
}
