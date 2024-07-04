using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Utils;

namespace Shokofin.Providers;

public class TrailerProvider: IRemoteMetadataProvider<Trailer, TrailerInfo>, IHasOrder
{
    public string Name => Plugin.MetadataProviderName;

    // Always run first, so we can react to the VFS entries.
    public int Order => -1;

    private readonly IHttpClientFactory HttpClientFactory;

    private readonly ILogger<TrailerProvider> Logger;

    private readonly ShokoAPIManager ApiManager;

    public TrailerProvider(IHttpClientFactory httpClientFactory, ILogger<TrailerProvider> logger, ShokoAPIManager apiManager)
    {
        HttpClientFactory = httpClientFactory;
        Logger = logger;
        ApiManager = apiManager;
    }

    public async Task<MetadataResult<Trailer>> GetMetadata(TrailerInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Trailer>();
        if (string.IsNullOrEmpty(info.Path) || !info.Path.StartsWith(Plugin.Instance.VirtualRoot + Path.DirectorySeparatorChar)) {
            return result;
        }

        var trackerId = Plugin.Instance.Tracker.Add($"Providing info for Trailer \"{info.Name}\". (Path=\"{info.Path}\")");
        try {
            var (fileInfo, seasonInfo, showInfo) = await ApiManager.GetFileInfoByPath(info.Path);
            var episodeInfo = fileInfo?.EpisodeList.FirstOrDefault().Episode;
            if (fileInfo == null || episodeInfo == null || seasonInfo == null || showInfo == null) {
                Logger.LogWarning("Unable to find episode info for path {Path}", info.Path);
                return result;
            }

            var (displayTitle, alternateTitle) = Text.GetEpisodeTitles(episodeInfo, seasonInfo, info.MetadataLanguage);
            var description = Text.GetDescription(episodeInfo);
            result.Item = new()
            {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                PremiereDate = episodeInfo.AniDB.AirDate,
                ProductionYear = episodeInfo.AniDB.AirDate?.Year ?? seasonInfo.AniDB.AirDate?.Year,
                Overview = description,
                CommunityRating = episodeInfo.AniDB.Rating.Value > 0 ? episodeInfo.AniDB.Rating.ToFloat(10) : 0,
            };
            Logger.LogInformation("Found trailer {EpisodeName} (File={FileId},Episode={EpisodeId},Series={SeriesId},Group={GroupId})", result.Item.Name, fileInfo.Id, episodeInfo.Id, seasonInfo.Id, showInfo?.GroupId);

            result.HasMetadata = true;

            return result;
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
            return new MetadataResult<Trailer>();
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(TrailerInfo searchInfo, CancellationToken cancellationToken)
        => Task.FromResult<IEnumerable<RemoteSearchResult>>(new List<RemoteSearchResult>());

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => HttpClientFactory.CreateClient().GetAsync(url, cancellationToken);
}
