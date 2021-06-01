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
        private readonly ISessionManager _sessionManager;
        private readonly ILogger<Scrobbler> _logger;

        public Scrobbler(ISessionManager sessionManager, ILogger<Scrobbler> logger)
        {
            _sessionManager = sessionManager;
            _logger = logger;
        }
        
        public Task RunAsync()
        {
            _sessionManager.PlaybackStopped += OnPlaybackStopped;
            return Task.CompletedTask;
        }

        private async void OnPlaybackStopped(object sender, PlaybackStopEventArgs e)
        {
            if (!Plugin.Instance.Configuration.UpdateWatchedStatus) return;
            
            if (e.Item == null)
            {
                _logger.LogError("Shoko Scrobbler... Event details incomplete. Cannot process current media");
                return;
            }

            if (e.Item is Episode episode && e.PlayedToCompletion)
            {
                var episodeId = episode.GetProviderId("Shoko Episode");
                
                _logger.LogInformation("Shoko Scrobbler... Item is played. Marking as watched on Shoko");
                _logger.LogInformation($"{episode.SeriesName} S{episode.Season.IndexNumber}E{episode.IndexNumber} - {episode.Name} ({episodeId})");

                var result = await ShokoAPI.MarkEpisodeWatched(episodeId);
                if (result)
                    _logger.LogInformation("Shoko Scrobbler... Episode marked as watched!");
                else
                    _logger.LogError("Shoko Scrobbler... Error marking episode as watched!");
            }
        }
        
        public void Dispose()
        {
            _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        }
    }
}