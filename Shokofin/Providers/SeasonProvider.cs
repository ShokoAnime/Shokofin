using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.Extensions;
using Shokofin.ExternalIds;
using Shokofin.Utils;

using Info = Shokofin.API.Info;

namespace Shokofin.Providers;

public class SeasonProvider(IHttpClientFactory _httpClientFactory, ILogger<SeasonProvider> _logger, ShokoApiManager _apiManager) : IRemoteMetadataProvider<Season, SeasonInfo>, IHasOrder {
    public string Name => Plugin.MetadataProviderName;

    public int Order => 0;

    public async Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken cancellationToken) {
        var result = new MetadataResult<Season>();
        if (!info.IndexNumber.HasValue)
            return result;

        // Special handling of the "Specials" season (pun intended).
        if (info.IndexNumber.Value == 0) {
            // We're forcing the sort names to start with "AA" to make it
            // always appear first in the UI.
            var seasonName = info.Name;
            result.Item = new Season {
                Name = seasonName,
                IndexNumber = info.IndexNumber,
                SortName = $"AA - {seasonName}",
                ForcedSortName = $"AA - {seasonName}",
            };
            result.HasMetadata = true;

            return result;
        }

        if (!info.TryGetSeasonId(out var seasonId)) {
            _logger.LogDebug("Unable refresh Season {SeasonNumber} {SeasonName}", info.IndexNumber, info.Name);
            return result;
        }

        var seasonNumber = info.IndexNumber.Value;
        var trackerId = Plugin.Instance.Tracker.Add($"Providing info for Season \"{info.Name}\". (Path=\"{info.Path}\",Series=\"{seasonId}\",Season={seasonNumber})");
        try {
            var showInfo = await _apiManager.GetShowInfoBySeasonId(seasonId).ConfigureAwait(false);
            if (showInfo == null) {
                _logger.LogWarning("Unable to find show info for Season {SeasonNumber}. (MainSeason={MainSeasonId})", seasonNumber, seasonId);
                return result;
            }

            var seasonInfo = showInfo.GetSeasonInfoBySeasonNumber(seasonNumber);
            if (seasonInfo == null || !showInfo.TryGetBaseSeasonNumberForSeasonInfo(seasonInfo, out var baseSeasonNumber)) {
                _logger.LogWarning("Unable to find series info for Season {SeasonNumber}. (MainSeason={MainSeasonId})", seasonNumber, seasonId);
                return result;
            }

            _logger.LogInformation("Found info for Season {SeasonNumber} in Series {SeriesName} (MainSeason={MainSeasonId})", seasonNumber, showInfo.Title, seasonId);

            var offset = Math.Abs(seasonNumber - baseSeasonNumber);

            result.Item = CreateMetadata(seasonInfo, seasonNumber, offset, info.MetadataLanguage, info.MetadataCountryCode);
            result.HasMetadata = true;
            result.ResetPeople();
            foreach (var person in seasonInfo.Staff)
                result.AddPerson(person);

            return result;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Threw unexpectedly while refreshing season {SeasonNumber}; {Message} (Path={Path},MainSeason={MainSeasonId})", info.IndexNumber, ex.Message, info.Path, seasonId);
            return new MetadataResult<Season>();
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    public static Season CreateMetadata(Info.SeasonInfo seasonInfo, int seasonNumber, int offset, string metadataLanguage, string metadataCountryCode)
        => CreateMetadata(seasonInfo, seasonNumber, offset, metadataLanguage, metadataCountryCode, null, Guid.Empty);

    public static Season CreateMetadata(Info.SeasonInfo seasonInfo, int seasonNumber, int offset, Series series, Guid seasonId)
        => CreateMetadata(seasonInfo, seasonNumber, offset, series.GetPreferredMetadataLanguage(), series.GetPreferredMetadataCountryCode(), series, seasonId);

    private static Season CreateMetadata(Info.SeasonInfo seasonInfo, int seasonNumber, int offset, string metadataLanguage, string metadataCountryCode, Series? series, Guid seasonId) {
        var (displayTitle, alternateTitle) = TextUtility.GetSeasonTitles(seasonInfo, offset, metadataLanguage);
        if (string.IsNullOrEmpty(displayTitle))
            displayTitle = $"Season {seasonNumber}";

        var sortTitle = $"S{seasonNumber} - {seasonInfo.Title}";
        Season season;
        if (series != null) {
            season = new Season {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                IndexNumber = seasonNumber,
                SortName = sortTitle,
                ForcedSortName = sortTitle,
                Id = seasonId,
                IsVirtualItem = true,
                Overview = TextUtility.GetSeasonDescription(seasonInfo, metadataLanguage),
                PremiereDate = seasonInfo.PremiereDate,
                EndDate = seasonInfo.EndDate,
                ProductionYear = seasonInfo.PremiereDate?.Year,
                Tags = seasonInfo.Tags.ToArray(),
                Genres = seasonInfo.Genres.ToArray(),
                Studios = seasonInfo.Studios.ToArray(),
                ProductionLocations = TagFilter.GetProductionLocations(seasonInfo),
                OfficialRating = ContentRating.GetContentRating(seasonInfo, metadataCountryCode),
                CommunityRating = seasonInfo.CommunityRating.ToFloat(10),
                SeriesId = series.Id,
                SeriesName = series.Name,
                SeriesPresentationUniqueKey = series.GetPresentationUniqueKey(),
                DateModified = DateTime.UtcNow,
                DateLastSaved = DateTime.UtcNow,
            };
        }
        else {
            season = new Season {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                IndexNumber = seasonNumber,
                SortName = sortTitle,
                ForcedSortName = sortTitle,
                Overview = TextUtility.GetSeasonDescription(seasonInfo, metadataLanguage),
                PremiereDate = seasonInfo.PremiereDate,
                EndDate = seasonInfo.EndDate,
                ProductionYear = seasonInfo.PremiereDate?.Year,
                Tags = seasonInfo.Tags.ToArray(),
                Genres = seasonInfo.Genres.ToArray(),
                Studios = seasonInfo.Studios.ToArray(),
                ProductionLocations = TagFilter.GetProductionLocations(seasonInfo),
                OfficialRating = ContentRating.GetContentRating(seasonInfo, metadataCountryCode),
                CommunityRating = seasonInfo.CommunityRating?.ToFloat(10),
            };
        }

        season.SetProviderId(ShokoInternalId.Name, seasonInfo.InternalId);
        if (seasonInfo.ShokoSeriesId is { Length: > 0 } shokoSeriesId)
            season.SetProviderId(ProviderNames.ShokoSeries, shokoSeriesId);
        if (Plugin.Instance.Configuration.AddAniDBId && seasonInfo.AnidbAnimeId is { Length: > 0 } anidbAnimeId)
            season.SetProviderId(ProviderNames.Anidb, anidbAnimeId);

        return season;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo searchInfo, CancellationToken cancellationToken)
        => Task.FromResult<IEnumerable<RemoteSearchResult>>([]);

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
}

