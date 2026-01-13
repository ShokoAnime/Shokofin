using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Extensions;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.ExternalIds;
using Shokofin.Utils;

using Info = Shokofin.API.Info;
using SeriesType = Shokofin.API.Models.SeriesType;
using EpisodeType = Shokofin.API.Models.EpisodeType;

namespace Shokofin.Providers;

public class EpisodeProvider(IHttpClientFactory _httpClientFactory, ILogger<EpisodeProvider> _logger, ShokoApiManager _apiManager) : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder {
    public string Name => Plugin.MetadataProviderName;

    public int Order => 0;

    public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken) {
        var trackerId = Plugin.Instance.Tracker.Add($"Providing info for Episode \"{info.Name}\". (Path=\"{info.Path}\",IsMissingEpisode={info.IsMissingEpisode})");
        try {
            var result = new MetadataResult<Episode>();
            var config = Plugin.Instance.Configuration;

            // Fetch the episode, series and group info (and file info, but that's not really used (yet))
            Info.FileInfo? fileInfo = null;
            Info.EpisodeInfo? episodeInfo = null;
            Info.SeasonInfo? seasonInfo = null;
            Info.ShowInfo? showInfo = null;
            if (info.IsMissingEpisode || string.IsNullOrEmpty(info.Path)) {
                // We're unable to fetch the latest metadata for the virtual episode.
                if (!info.TryGetProviderId(ProviderNames.ShokoEpisode, out var episodeId))
                    return result;

                episodeInfo = await _apiManager.GetEpisodeInfo(episodeId).ConfigureAwait(false);
                if (episodeInfo == null)
                    return result;

                seasonInfo = await _apiManager.GetSeasonInfoForEpisode(episodeId).ConfigureAwait(false);
                if (seasonInfo == null)
                    return result;

                showInfo = await _apiManager.GetShowInfoBySeasonId(seasonInfo.Id).ConfigureAwait(false);
                if (showInfo == null || showInfo.SeasonList.Count == 0)
                    return result;
            }
            else {
                (fileInfo, seasonInfo, showInfo) = await _apiManager.GetFileInfoByPath(info.Path).ConfigureAwait(false);
                episodeInfo = fileInfo is { EpisodeList.Count: > 0 } ? fileInfo.EpisodeList[0].Episode : null;
            }

            // if the episode info is null then the series info and conditionally the group info is also null.
            if (episodeInfo == null || seasonInfo == null || showInfo == null) {
                _logger.LogWarning("Unable to find episode info for path {Path}", info.Path);
                return result;
            }

            result.Item = CreateMetadata(showInfo, seasonInfo, episodeInfo, fileInfo, info.MetadataLanguage, info.MetadataCountryCode);
            _logger.LogInformation("Found episode {EpisodeName} (File={FileId},Episode={EpisodeId},Season={SeasonId},ExtraSeasons={ExtraIds})", result.Item.Name, fileInfo?.Id, episodeInfo.Id, seasonInfo.Id, seasonInfo.ExtraIds);

            result.HasMetadata = true;

            result.ResetPeople();
            foreach (var person in episodeInfo.Staff)
                result.AddPerson(person);

            return result;
        }
        catch (Exception ex) {
            if (info.IsMissingEpisode || string.IsNullOrEmpty(info.Path)) {
                if (!info.TryGetProviderId(ProviderNames.ShokoEpisode, out var episodeId))
                    episodeId = null;

                _logger.LogError(ex, "Threw unexpectedly while refreshing a missing episode; {Message} (Episode={EpisodeId})", ex.Message, episodeId);
            }
            else {
                _logger.LogError(ex, "Threw unexpectedly while refreshing {Path}: {Message}", info.Path, ex.Message);
            }

            return new MetadataResult<Episode>();
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    public static Episode CreateMetadata(Info.ShowInfo showInfo, Info.SeasonInfo seasonInfo, Info.EpisodeInfo episodeInfo, Season season, Guid episodeId)
        => CreateMetadata(showInfo, seasonInfo, episodeInfo, null, season.GetPreferredMetadataLanguage(), season.GetPreferredMetadataCountryCode(), season, episodeId);

    public static Episode CreateMetadata(Info.ShowInfo showInfo, Info.SeasonInfo seasonInfo, Info.EpisodeInfo episodeInfo, Info.FileInfo? file, string metadataLanguage, string metadataCountryCode)
        => CreateMetadata(showInfo, seasonInfo, episodeInfo, file, metadataLanguage, metadataCountryCode, null, Guid.Empty);

    private static Episode CreateMetadata(Info.ShowInfo showInfo, Info.SeasonInfo seasonInfo, Info.EpisodeInfo episodeInfo, Info.FileInfo? fileInfo, string metadataLanguage, string metadataCountryCode, Season? season, Guid episodeId) {
        var config = Plugin.Instance.Configuration;
        var episodeNumber = Ordering.GetEpisodeNumber(showInfo, seasonInfo, episodeInfo);
        var seasonNumber = Ordering.GetSeasonNumber(showInfo, seasonInfo, episodeInfo);
        var (airsBeforeEpisodeNumber, airsBeforeSeasonNumber, airsAfterSeasonNumber, isSpecial) = Ordering.GetSpecialPlacement(showInfo, seasonInfo, episodeInfo);
        string? displayTitle, alternateTitle, description;
        if (fileInfo is not null && fileInfo.EpisodeList.Count > 1) {
            var displayTitles = new List<string?>();
            var alternateTitles = new List<string?>();
            foreach (var (eI, _, _) in fileInfo.EpisodeList) {
                string defaultEpisodeTitle = eI.Title;
                string? dTitle, aTitle;
                if (
                    // Movies
                    (seasonInfo.Type == SeriesType.Movie && eI.Type is EpisodeType.Normal or EpisodeType.Special) ||
                    // All other ignored types.
                    (
                        eI.Type is EpisodeType.Normal &&
                        eI.EpisodeNumber == 1 &&
                        eI.Titles.FirstOrDefault(title => title.Source is "AniDB" && title.LanguageCode is "en")?.Value is { } mainTitle &&
                        TextUtility.IgnoredSubTitles.Contains(mainTitle) &&
                        TextUtility.GetEpisodeTitles(eI, seasonInfo, metadataLanguage) is { } episodeTitles &&
                        string.IsNullOrEmpty(episodeTitles.displayTitle)
                    )
                )
                    (dTitle, aTitle) = TextUtility.GetMovieTitles(eI, seasonInfo, metadataLanguage);
                else
                    (dTitle, aTitle) = TextUtility.GetEpisodeTitles(eI, seasonInfo, metadataLanguage);

                if (string.IsNullOrEmpty(dTitle))
                    dTitle = eI.Type switch {
                        EpisodeType.Special => $"Special {Ordering.GetEpisodeNumber(showInfo, seasonInfo, eI)}",
                        _ => $"Episode {Ordering.GetEpisodeNumber(showInfo, seasonInfo, eI)}",
                    };

                displayTitles.Add(dTitle);
                alternateTitles.Add(aTitle);
            }
            displayTitle = TextUtility.JoinText(displayTitles);
            alternateTitle = TextUtility.JoinText(alternateTitles);
            description = TextUtility.GetEpisodeDescription(fileInfo.EpisodeList.Select(tuple => tuple.Episode), seasonInfo, metadataLanguage);
        }
        else {
            string defaultEpisodeTitle = episodeInfo.Title;
            if (
                // Movies
                (seasonInfo.Type == SeriesType.Movie && episodeInfo.Type is EpisodeType.Normal or EpisodeType.Special) ||
                // All other ignored types.
                (
                    episodeInfo.Type is EpisodeType.Normal &&
                    episodeInfo.EpisodeNumber == 1 &&
                    episodeInfo.Titles.FirstOrDefault(title => title.Source is "AniDB" && title.LanguageCode is "en")?.Value is { } mainTitle &&
                    TextUtility.IgnoredSubTitles.Contains(mainTitle) &&
                    TextUtility.GetEpisodeTitles(episodeInfo, seasonInfo, metadataLanguage) is { } episodeTitles &&
                    string.IsNullOrEmpty(episodeTitles.displayTitle)
                )
            )
                (displayTitle, alternateTitle) = TextUtility.GetMovieTitles(episodeInfo, seasonInfo, metadataLanguage);
            else
                (displayTitle, alternateTitle) = TextUtility.GetEpisodeTitles(episodeInfo, seasonInfo, metadataLanguage);

            if (string.IsNullOrEmpty(displayTitle))
                displayTitle = episodeInfo.Type switch {
                    EpisodeType.Special => $"Special {episodeNumber}",
                    _ => $"Episode {episodeNumber}",
                };

            description = TextUtility.GetEpisodeDescription(episodeInfo, seasonInfo, metadataLanguage);
        }

        if (isSpecial && config.MarkSpecialsWhenGrouped) {
            // We're guaranteed to find the index, because otherwise it would've thrown when getting the episode number.
            var index = seasonInfo.SpecialsList.FindIndex(ep => ep == episodeInfo);
            displayTitle = $"S{index + 1} {displayTitle}";
            alternateTitle = $"S{index + 1} {alternateTitle}";
        }

        Episode result;
        if (season is not null) {
            result = new Episode {
                Name = displayTitle ?? $"Episode {episodeNumber}",
                OriginalTitle = alternateTitle ?? "",
                IndexNumber = episodeNumber,
                ParentIndexNumber = isSpecial ? 0 : seasonNumber,
                AirsAfterSeasonNumber = airsAfterSeasonNumber,
                AirsBeforeEpisodeNumber = airsBeforeEpisodeNumber,
                AirsBeforeSeasonNumber = airsBeforeSeasonNumber,
                Id = episodeId,
                IsVirtualItem = true,
                SeasonId = season.Id,
                SeriesId = season.Series.Id,
                Overview = description,
                CommunityRating = episodeInfo.CommunityRating.Value > 0 ? episodeInfo.CommunityRating.ToFloat(10) : 0,
                PremiereDate = episodeInfo.AiredAt,
                SeriesName = season.Series.Name,
                SeriesPresentationUniqueKey = season.SeriesPresentationUniqueKey,
                SeasonName = season.Name,
                ProductionLocations = TagFilter.GetProductionLocations(episodeInfo),
                OfficialRating = ContentRating.GetContentRating(episodeInfo, metadataCountryCode),
                DateLastSaved = DateTime.UtcNow,
                RunTimeTicks = episodeInfo.Runtime?.Ticks,
            };
            result.PresentationUniqueKey = result.GetPresentationUniqueKey();
        }
        else {
            result = new Episode {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
                IndexNumber = episodeNumber,
                ParentIndexNumber = isSpecial ? 0 : seasonNumber,
                AirsAfterSeasonNumber = airsAfterSeasonNumber,
                AirsBeforeEpisodeNumber = airsBeforeEpisodeNumber,
                AirsBeforeSeasonNumber = airsBeforeSeasonNumber,
                PremiereDate = episodeInfo.AiredAt,
                Overview = description,
                ProductionLocations = TagFilter.GetProductionLocations(episodeInfo),
                OfficialRating = ContentRating.GetContentRating(episodeInfo, metadataCountryCode),
                CustomRating = showInfo.CustomRating,
                CommunityRating = episodeInfo.CommunityRating.Value > 0 ? episodeInfo.CommunityRating.ToFloat(10) : 0,
            };
        }

        if (fileInfo is not null && fileInfo.EpisodeList.Count > 1) {
            var episodeNumberEnd = episodeNumber + fileInfo.EpisodeList.Count - 1;
            if (episodeNumberEnd != episodeNumber && episodeInfo.EpisodeNumber != episodeNumberEnd)
                result.IndexNumberEnd = episodeNumberEnd;
        }

        if (fileInfo is not null) {
            result.SetProviderId(ShokoInternalId.Name, fileInfo.InternalId);
            result.SetProviderId(ProviderNames.Shoko, ShokoExternalUrlHandler.GetFileInfoUrls(fileInfo));
            result.SetProviderId(ProviderNames.ShokoFile, fileInfo.Id);
            result.SetProviderId(ProviderNames.ShokoSeries, fileInfo.SeriesId);
        }
        else {
            result.SetProviderId(ShokoInternalId.Name, episodeInfo.InternalId);
            result.SetProviderId(ProviderNames.Shoko, ShokoExternalUrlHandler.GetEpisodeInfoUrls(episodeInfo));
        }
        result.SetProviderId(ProviderNames.ShokoEpisode, episodeInfo.Id);
        if (config.AddAniDBId && episodeInfo.AnidbEpisodeId is { Length: > 0 } anidbEpisodeId)
            result.SetProviderId(ProviderNames.Anidb, anidbEpisodeId);
        if (config.AddTMDBId && episodeInfo.TmdbEpisodeId is { Length: > 0 } tmdbEpisodeId)
            result.SetProviderId(MetadataProvider.Tmdb, tmdbEpisodeId);
        if (config.AddTvDBId && episodeInfo.TvdbEpisodeId is { Length: > 0 } tvdbEpisodeId)
            result.SetProviderId(MetadataProvider.Tvdb, tvdbEpisodeId);

        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        => Task.FromResult<IEnumerable<RemoteSearchResult>>([]);

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
}
