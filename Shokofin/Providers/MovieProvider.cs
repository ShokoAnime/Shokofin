using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.ExternalIds;
using Shokofin.Utils;

namespace Shokofin.Providers;

public class MovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>, IHasOrder
{
    public string Name => Plugin.MetadataProviderName;

    public int Order => 0;

    private readonly IHttpClientFactory HttpClientFactory;

    private readonly ILogger<MovieProvider> Logger;

    private readonly ShokoAPIManager ApiManager;

    public MovieProvider(IHttpClientFactory httpClientFactory, ILogger<MovieProvider> logger, ShokoAPIManager apiManager)
    {
        Logger = logger;
        HttpClientFactory = httpClientFactory;
        ApiManager = apiManager;
    }

    public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
    {
        var trackerId = Plugin.Instance.Tracker.Add($"Providing info for Movie \"{info.Name}\". (Path=\"{info.Path}\")");
        try {
            var result = new MetadataResult<Movie>();
            var (file, season, _) = await ApiManager.GetFileInfoByPath(info.Path);
            var episode = file?.EpisodeList.FirstOrDefault().Episode;

            // if file is null then series and episode is also null.
            if (file == null || episode == null || season == null) {
                Logger.LogWarning("Unable to find movie info for path {Path}", info.Path);
                return result;
            }

            var (displayTitle, alternateTitle) = Text.GetMovieTitles(episode, season, info.MetadataLanguage);
            Logger.LogInformation("Found movie {EpisodeName} (File={FileId},Episode={EpisodeId},Series={SeriesId},ExtraSeries={ExtraIds})", displayTitle, file.Id, episode.Id, season.Id, season.ExtraIds);

            bool isMultiEntry = season.Shoko.Sizes.Total.Episodes > 1;
            bool isMainEntry = episode.AniDB.Type == API.Models.EpisodeType.Normal && episode.Shoko.Name.Trim() == "Complete Movie";
            var rating = isMultiEntry ? episode.AniDB.Rating.ToFloat(10) : season.AniDB.Rating.ToFloat(10);

            result.Item = new Movie {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                PremiereDate = episode.AniDB.AirDate,
                // Use the file description if collection contains more than one movie and the file is not the main entry, otherwise use the collection description.
                Overview = isMultiEntry && !isMainEntry ? Text.GetDescription(episode) : Text.GetDescription(season),
                ProductionYear = episode.AniDB.AirDate?.Year,
                Tags = season.Tags.ToArray(),
                Genres = season.Genres.ToArray(),
                Studios = season.Studios.ToArray(),
                ProductionLocations = TagFilter.GetMovieContentRating(season, episode).ToArray(),
                OfficialRating = ContentRating.GetMovieContentRating(season, episode),
                CommunityRating = rating,
                DateCreated = file.Shoko.ImportedAt ?? file.Shoko.CreatedAt,
            };
            result.Item.SetProviderId(ShokoFileId.Name, file.Id);
            result.Item.SetProviderId(ShokoEpisodeId.Name, episode.Id);
            result.Item.SetProviderId(ShokoSeriesId.Name, season.Id);

            result.HasMetadata = true;

            result.ResetPeople();
            foreach (var person in season.Staff)
                result.AddPerson(person);

            return result;
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
            return new MetadataResult<Movie>();
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
        => Task.FromResult<IEnumerable<RemoteSearchResult>>([]);

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => HttpClientFactory.CreateClient().GetAsync(url, cancellationToken);
}
