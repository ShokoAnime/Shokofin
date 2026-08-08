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
            var showInfo = await _apiManager.GetShowInfoByPath(info.Path);
            if (showInfo == null) {
                try {
                    // Look for the "season" directories to probe for the group information
                    var entries = _fileSystem.GetDirectories(info.Path, false);
                    foreach (var entry in entries) {
                        showInfo = await _apiManager.GetShowInfoByPath(entry.FullName);
                        if (showInfo is not null)
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

            // If using Shoko titles/images for TMDB structure is enabled,
            // find the main shoko series for this show and use it to generate titles.
            API.Info.ShowInfo? titleOverrideShowInfo = null;
            if (
                showInfo.DefaultSeason.StructureType == Configuration.SeriesStructureType.TMDB_SeriesAndMovies &&
                Plugin.Instance.Configuration.TmdbStructureUseShokoMetadata
            ) {
                titleOverrideShowInfo = await _apiManager.GetMainShokoSeriesShowInfo(showInfo.ShokoSeriesId);
            }

            var (displayTitle, alternateTitle) = TextUtility.GetShowTitles(titleOverrideShowInfo ?? showInfo, info.MetadataLanguage);
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

            var config = Plugin.Instance.Configuration;
            result.Item.SetProviderId(ShokoInternalId.Name, showInfo.InternalId);
            result.Item.SetProviderId(ProviderNames.Shoko, ShokoExternalUrlHandler.GetShowInfoUrls(showInfo));
            if (showInfo.ShokoSeriesId is { Length: > 0 } shokoSeriesId)
                result.Item.SetProviderId(ProviderNames.ShokoSeries, shokoSeriesId);
            if (showInfo.ShokoGroupId is { Length: > 0 } shokoGroupId)
                result.Item.SetProviderId(ProviderNames.ShokoGroup, shokoGroupId);
            if (config.AddAniDBId && showInfo.AnidbAnimeId is { Length: > 0 } anidbAnimeId)
                result.Item.SetProviderId(ProviderNames.Anidb, anidbAnimeId);
            if (config.AddTMDBId && showInfo.TmdbShowId is { Length: > 0 } tmdbShowId)
                result.Item.SetProviderId(MetadataProvider.Tmdb, tmdbShowId);
            if (config.AddTvDBId && showInfo.TvdbShowId is { Length: > 0 } tvdbShowId)
                result.Item.SetProviderId(MetadataProvider.Tvdb, tvdbShowId);

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

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo info, CancellationToken cancellationToken)
        => Task.FromResult<IEnumerable<RemoteSearchResult>>([]);

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => await _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
}
