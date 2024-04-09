using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.ExternalIds;
using Shokofin.Utils;

namespace Shokofin.Providers;

public class SeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>
{
    public string Name => Plugin.MetadataProviderName;

    private readonly IHttpClientFactory HttpClientFactory;

    private readonly ILogger<SeriesProvider> Logger;

    private readonly ShokoAPIManager ApiManager;

    private readonly IFileSystem FileSystem;

    public SeriesProvider(IHttpClientFactory httpClientFactory, ILogger<SeriesProvider> logger, ShokoAPIManager apiManager, IFileSystem fileSystem)
    {
        Logger = logger;
        HttpClientFactory = httpClientFactory;
        ApiManager = apiManager;
        FileSystem = fileSystem;
    }

    public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
    {
        try {
            var result = new MetadataResult<Series>();
            var show = await ApiManager.GetShowInfoByPath(info.Path);
            if (show == null) {
                try {
                    // Look for the "season" directories to probe for the group information
                    var entries = FileSystem.GetDirectories(info.Path, false);
                    foreach (var entry in entries) {
                        show = await ApiManager.GetShowInfoByPath(entry.FullName);
                        if (show != null)
                            break;
                    }
                    if (show == null) {
                        Logger.LogWarning("Unable to find show info for path {Path}", info.Path);
                        return result;
                    }
                }
                catch (DirectoryNotFoundException) {
                    return result;
                }
            }

            var (displayTitle, alternateTitle) = Text.GetShowTitles(show, info.MetadataLanguage);
            var premiereDate = show.PremiereDate;
            var endDate = show.EndDate;
            result.Item = new Series {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                Overview = Text.GetDescription(show),
                PremiereDate = premiereDate,
                ProductionYear = premiereDate?.Year,
                EndDate = endDate,
                Status = !endDate.HasValue || endDate.Value > DateTime.UtcNow ? SeriesStatus.Continuing : SeriesStatus.Ended,
                Tags = show.Tags.ToArray(),
                Genres = show.Genres.ToArray(),
                Studios = show.Studios.ToArray(),
                OfficialRating = show.OfficialRating,
                CustomRating = show.CustomRating,
                CommunityRating = show.CommunityRating,
            };
            result.HasMetadata = true;
            result.ResetPeople();
            foreach (var person in show.Staff)
                result.AddPerson(person);

            AddProviderIds(result.Item, show.Id, show.GroupId, show.DefaultSeason.AniDB.Id.ToString());

            Logger.LogInformation("Found series {SeriesName} (Series={SeriesId},Group={GroupId})", displayTitle, show.Id, show.GroupId);

            return result;
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
            return new MetadataResult<Series>();
        }
    }

    public static void AddProviderIds(IHasProviderIds item, string seriesId, string? groupId = null, string? anidbId = null, string? tmdbId = null)
    {
        item.SetProviderId(MetadataProvider.Custom, $"shoko://shoko-series={seriesId}");

        var config = Plugin.Instance.Configuration;
        item.SetProviderId(ShokoSeriesId.Name, seriesId);
        if (!string.IsNullOrEmpty(groupId))
            item.SetProviderId(ShokoGroupId.Name, groupId);
        if (config.AddAniDBId && !string.IsNullOrEmpty(anidbId) && anidbId != "0")
            item.SetProviderId("AniDB", anidbId);
        if (config.AddTMDBId &&!string.IsNullOrEmpty(tmdbId) && tmdbId != "0")
            item.SetProviderId(MetadataProvider.Tmdb, tmdbId);
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo info, CancellationToken cancellationToken)
        => Task.FromResult<IEnumerable<RemoteSearchResult>>(new List<RemoteSearchResult>());

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => HttpClientFactory.CreateClient().GetAsync(url, cancellationToken);
}
