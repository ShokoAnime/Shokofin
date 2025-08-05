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

public class SeriesProvider(IHttpClientFactory _httpClientFactory, ILogger<SeriesProvider> _logger, ShokoApiManager _apiManager, IFileSystem _fileSystem) : IRemoteMetadataProvider<Series, SeriesInfo>, IHasOrder {
    public string Name => Plugin.MetadataProviderName;

    public int Order => 0;

    public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken) {
        var trackerId = Plugin.Instance.Tracker.Add($"Providing info for Series \"{info.Name}\". (Path=\"{info.Path}\")");
        try {
            var result = new MetadataResult<Series>();
            var showInfo = await _apiManager.GetShowInfoByPath(info.Path).ConfigureAwait(false);
            if (showInfo == null) {
                try {
                    // Look for the "season" directories to probe for the group information
                    var entries = _fileSystem.GetDirectories(info.Path, false);
                    foreach (var entry in entries) {
                        showInfo = await _apiManager.GetShowInfoByPath(entry.FullName).ConfigureAwait(false);
                        if (showInfo != null)
                            break;
                    }
                    if (showInfo == null) {
                        _logger.LogWarning("Unable to find show info for path {Path}", info.Path);
                        return result;
                    }
                }
                catch (DirectoryNotFoundException) {
                    return result;
                }
            }

            var (displayTitle, alternateTitle) = TextUtility.GetShowTitles(showInfo, info.MetadataLanguage);
            if (string.IsNullOrEmpty(displayTitle))
                displayTitle = showInfo.Title;

            var premiereDate = showInfo.PremiereDate;
            var endDate = showInfo.EndDate;
            result.Item = new Series {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                Overview = TextUtility.GetShowDescription(showInfo, info.MetadataLanguage),
                PremiereDate = premiereDate,
                AirDays = showInfo.DaysOfWeek.ToArray(),
                ProductionYear = premiereDate?.Year,
                EndDate = endDate,
                Status = !endDate.HasValue || endDate.Value > DateTime.UtcNow ? SeriesStatus.Continuing : SeriesStatus.Ended,
                Tags = showInfo.Tags.ToArray(),
                Genres = showInfo.Genres.ToArray(),
                Studios = showInfo.Studios.ToArray(),
                ProductionLocations = TagFilter.GetProductionLocations(showInfo),
                OfficialRating = ContentRating.GetContentRating(showInfo, info.MetadataCountryCode),
                CustomRating = showInfo.CustomRating,
                CommunityRating = showInfo.CommunityRating,
            };
            result.HasMetadata = true;
            result.ResetPeople();
            foreach (var person in showInfo.Staff)
                result.AddPerson(person);

            AddProviderIds(result.Item, showInfo.InternalId, seriesId: showInfo.ShokoSeriesId, groupId: showInfo.ShokoGroupId, anidbId: showInfo.AnidbAnimeId, tmdbId: showInfo.TmdbShowId, tvdbId: showInfo.TvdbShowId);

            _logger.LogInformation("Found series {SeriesName} (MainSeason={MainSeasonId})", displayTitle, showInfo.Id);

            return result;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Threw unexpectedly while refreshing {Path}; {Message}", info.Path, ex.Message);
            return new MetadataResult<Series>();
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    public static void AddProviderIds(IHasProviderIds item, string internalId, string? seriesId = null, string? groupId = null, string? anidbId = null, string? tmdbId = null, string? tvdbId = null) {
        var config = Plugin.Instance.Configuration;

        item.SetProviderId(ShokoInternalId.Name, internalId);
        if (!string.IsNullOrEmpty(seriesId))
            item.SetProviderId(ProviderNames.ShokoSeries, seriesId);
        if (!string.IsNullOrEmpty(groupId))
            item.SetProviderId(ProviderNames.ShokoGroup, groupId);
        if (config.AddAniDBId && !string.IsNullOrEmpty(anidbId))
            item.SetProviderId(ProviderNames.Anidb, anidbId);
        if (config.AddTMDBId && !string.IsNullOrEmpty(tmdbId))
            item.SetProviderId(MetadataProvider.Tmdb, tmdbId);
        if (config.AddTvDBId && !string.IsNullOrEmpty(tvdbId))
            item.SetProviderId(MetadataProvider.Tvdb, tvdbId);
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo info, CancellationToken cancellationToken)
        => Task.FromResult<IEnumerable<RemoteSearchResult>>([]);

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
}
