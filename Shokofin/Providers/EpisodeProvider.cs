using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Utils;

using Path = System.IO.Path;
using IResolverIgnoreRule = MediaBrowser.Controller.Resolvers.IResolverIgnoreRule;
using ILibraryManager = MediaBrowser.Controller.Library.ILibraryManager;
using EpisodeType = Shokofin.API.Models.EpisodeType;

namespace Shokofin.Providers
{
    public class EpisodeProvider: IRemoteMetadataProvider<Episode, EpisodeInfo>, IResolverIgnoreRule
    {
        public string Name => "Shoko";

        private readonly IHttpClientFactory _httpClientFactory;

        private readonly ILogger<EpisodeProvider> _logger;

        private readonly ILibraryManager _library;

        public EpisodeProvider(IHttpClientFactory httpClientFactory, ILogger<EpisodeProvider> logger, ILibraryManager library)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _library = library;
        }

        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            try
            {
                var result = new MetadataResult<Episode>();

                var includeGroup = Plugin.Instance.Configuration.SeriesGrouping == Ordering.SeriesOrBoxSetGroupType.ShokoGroup;
                var (file, episode, series, group) = await DataFetcher.GetFileInfoByPath(info.Path, includeGroup);

                if (file == null) // if file is null then series and episode is also null.
                {
                    _logger.LogWarning($"Shoko Scanner... Unable to find file info for path {info.Path}");
                    return result;
                }
                _logger.LogInformation($"Shoko Scanner... Found file info for path {info.Path}");

                var ( displayTitle, alternateTitle ) = Text.GetEpisodeTitles(series.AniDB.Titles, episode.AniDB.Titles, episode.Shoko.Name, info.MetadataLanguage);
                int aniDBId = episode.AniDB.ID;
                int tvdbId = episode?.TvDB?.ID ?? 0;
                if (group != null && episode.AniDB.Type != EpisodeType.Normal && Plugin.Instance.Configuration.MarkSpecialsWhenGrouped) {
                    displayTitle = $"SP {episode.AniDB.EpisodeNumber} {displayTitle}";
                    alternateTitle = $"SP {episode.AniDB.EpisodeNumber} {alternateTitle}";
                }
                result.Item = new Episode
                {
                    IndexNumber = Ordering.GetIndexNumber(series, episode),
                    ParentIndexNumber = Ordering.GetSeasonNumber(group, series, episode),
                    Name = displayTitle,
                    OriginalTitle = alternateTitle,
                    PremiereDate = episode.AniDB.AirDate,
                    Overview = Text.SummarySanitizer(episode.AniDB.Description),
                    CommunityRating = (float) ((episode.AniDB.Rating.Value * 10) / episode.AniDB.Rating.MaxValue)
                };
                result.Item.SetProviderId("Shoko Episode", episode.ID);
                result.Item.SetProviderId("Shoko File", file.ID);
                result.Item.SetProviderId("AniDB", aniDBId.ToString());
                if (tvdbId != 0) result.Item.SetProviderId("Tvdb", tvdbId.ToString());
                result.HasMetadata = true;

                var episodeNumberEnd = episode.AniDB.EpisodeNumber + file.EpisodesCount - 1;
                if (episode.AniDB.EpisodeNumber != episodeNumberEnd) result.Item.IndexNumberEnd = episodeNumberEnd;

                return result;
            }
            catch (Exception e)
            {
                _logger.LogError($"{e.Message}{Environment.NewLine}{e.StackTrace}");
                return new MetadataResult<Episode>();
            }
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            // Isn't called from anywhere. If it is called, I don't know from where.
            throw new NotImplementedException();
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
        }

        public bool ShouldIgnore(MediaBrowser.Model.IO.FileSystemMetadata fileInfo, BaseItem parent)
        {
            // Skip this handler if one of these requirements are met
            if (fileInfo == null || parent == null || fileInfo.IsDirectory || !fileInfo.Exists || !(parent is Series || parent is Season))
                return false;
            var libType = _library.GetInheritedContentType(parent);
            if (libType != "tvshows") {
                return false;
            }
            try {
                var includeGroup = Plugin.Instance.Configuration.SeriesGrouping == Ordering.SeriesOrBoxSetGroupType.ShokoGroup;
                // TODO: Check if it can be written in a better way. Parent directory + File Name
                var id = Path.Join(fileInfo.DirectoryName, fileInfo.FullName);
                var (file, episode, series, group) = DataFetcher.GetFileInfoByPathSync(id, includeGroup);
                if (file == null) // if file is null then series and episode is also null.
                {
                    _logger.LogWarning($"Shoko Filter... Unable to find file info for path {id}");
                    return true;
                }
                _logger.LogInformation($"Shoko Filter... Found file info for path {id}");
                var extraType = Ordering.GetExtraType(episode.AniDB);
                if (extraType != null)
                {
                    _logger.LogDebug($"Shoko Filter... Not a normal or special episode, skipping path {id}");
                    return true;
                }
                return false;
            }
            catch (System.Exception e)
            {
                if (!(e is System.Net.Http.HttpRequestException && e.Message.Contains("Connection refused")))
                    _logger.LogError(e, "Threw unexpectedly");
                return false;
            }
        }
    }
}
