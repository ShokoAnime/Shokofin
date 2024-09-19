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
using Shokofin.ExternalIds;
using Shokofin.Utils;

using Info = Shokofin.API.Info;
using SeriesType = Shokofin.API.Models.SeriesType;
using EpisodeType = Shokofin.API.Models.EpisodeType;

namespace Shokofin.Providers;

public class EpisodeProvider: IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
{
    public string Name => Plugin.MetadataProviderName;

    public int Order => 0;

    private readonly IHttpClientFactory HttpClientFactory;

    private readonly ILogger<EpisodeProvider> Logger;

    private readonly ShokoAPIManager ApiManager;

    public EpisodeProvider(IHttpClientFactory httpClientFactory, ILogger<EpisodeProvider> logger, ShokoAPIManager apiManager)
    {
        HttpClientFactory = httpClientFactory;
        Logger = logger;
        ApiManager = apiManager;
    }

    public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
    {
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
                if (!info.TryGetProviderId(ShokoEpisodeId.Name, out var episodeId))
                    return result;

                episodeInfo = await ApiManager.GetEpisodeInfo(episodeId);
                if (episodeInfo == null)
                    return result;

                seasonInfo = await ApiManager.GetSeasonInfoForEpisode(episodeId);
                if (seasonInfo == null)
                    return result;

                showInfo = await ApiManager.GetShowInfoForSeries(seasonInfo.Id);
                if (showInfo == null || showInfo.SeasonList.Count == 0)
                    return result;
            }
            else {
                (fileInfo, seasonInfo, showInfo) = await ApiManager.GetFileInfoByPath(info.Path);
                episodeInfo = fileInfo?.EpisodeList.FirstOrDefault().Episode;
            }

            // if the episode info is null then the series info and conditionally the group info is also null.
            if (episodeInfo == null || seasonInfo == null || showInfo == null) {
                Logger.LogWarning("Unable to find episode info for path {Path}", info.Path);
                return result;
            }

            result.Item = CreateMetadata(showInfo, seasonInfo, episodeInfo, fileInfo, info.MetadataLanguage);
            Logger.LogInformation("Found episode {EpisodeName} (File={FileId},Episode={EpisodeId},Series={SeriesId},ExtraSeries={ExtraIds},Group={GroupId})", result.Item.Name, fileInfo?.Id, episodeInfo.Id, seasonInfo.Id, seasonInfo.ExtraIds, showInfo?.GroupId);

            result.HasMetadata = true;

            return result;
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
            return new MetadataResult<Episode>();
        }
        finally {
            Plugin.Instance.Tracker.Remove(trackerId);
        }
    }

    public static Episode CreateMetadata(Info.ShowInfo group, Info.SeasonInfo series, Info.EpisodeInfo episode, Season season, Guid episodeId)
        => CreateMetadata(group, series, episode, null, season.GetPreferredMetadataLanguage(), season, episodeId);

    public static Episode CreateMetadata(Info.ShowInfo group, Info.SeasonInfo series, Info.EpisodeInfo episode, Info.FileInfo? file, string metadataLanguage)
        => CreateMetadata(group, series, episode, file, metadataLanguage, null, Guid.Empty);

    private static Episode CreateMetadata(Info.ShowInfo group, Info.SeasonInfo series, Info.EpisodeInfo episode, Info.FileInfo? file, string metadataLanguage, Season? season, Guid episodeId)
    {
        var config = Plugin.Instance.Configuration;
        string? displayTitle, alternateTitle, description;
        if (file != null && file.EpisodeList.Count > 1) {
            var displayTitles = new List<string?>();
            var alternateTitles = new List<string?>();
            foreach (var (episodeInfo, _, _) in file.EpisodeList) {
                string defaultEpisodeTitle = episodeInfo.Shoko.Name;
                if (
                    // Movies
                    (series.Type == SeriesType.Movie && (episodeInfo.AniDB.Type == EpisodeType.Normal || episodeInfo.AniDB.Type == EpisodeType.Special)) ||
                    // OVAs
                    (series.AniDB.Type == SeriesType.OVA && episodeInfo.AniDB.Type == EpisodeType.Normal && episodeInfo.AniDB.EpisodeNumber == 1 && episodeInfo.Shoko.Name == "OVA")
                ) {
                    string defaultSeriesTitle = series.Shoko.Name;
                    var (dTitle, aTitle) = Text.GetMovieTitles(episodeInfo, series, metadataLanguage);
                    displayTitles.Add(dTitle);
                    alternateTitles.Add(aTitle);
                }
                else {
                    var (dTitle, aTitle) = Text.GetEpisodeTitles(episodeInfo, series, metadataLanguage);
                    displayTitles.Add(dTitle);
                    alternateTitles.Add(aTitle);
                }
            }
            displayTitle = Text.JoinText(displayTitles);
            alternateTitle = Text.JoinText(alternateTitles);
            description = Text.GetDescription(file.EpisodeList.Select(tuple => tuple.Episode));
        }
        else {
            string defaultEpisodeTitle = episode.Shoko.Name;
            if (
                // Movies
                (series.Type == SeriesType.Movie && (episode.AniDB.Type == EpisodeType.Normal || episode.AniDB.Type == EpisodeType.Special)) ||
                // OVAs
                (series.AniDB.Type == SeriesType.OVA && episode.AniDB.Type == EpisodeType.Normal && episode.AniDB.EpisodeNumber == 1 && episode.Shoko.Name == "OVA")
            ) {
                string defaultSeriesTitle = series.Shoko.Name;
                (displayTitle, alternateTitle) = Text.GetMovieTitles(episode, series, metadataLanguage);
            }
            else {
                (displayTitle, alternateTitle) = Text.GetEpisodeTitles(episode, series, metadataLanguage);
            }
            description = Text.GetDescription(episode);
        }

        if (config.MarkSpecialsWhenGrouped) switch (episode.AniDB.Type) {
            case EpisodeType.Other:
            case EpisodeType.Normal:
                break;
            case EpisodeType.Special: {
                // We're guaranteed to find the index, because otherwise it would've thrown when getting the episode number.
                var index = series.SpecialsList.FindIndex(ep => ep == episode);
                displayTitle = $"S{index + 1} {displayTitle}";
                alternateTitle = $"S{index + 1} {alternateTitle}";
                break;
            }
            case EpisodeType.ThemeSong:
            case EpisodeType.EndingSong:
            case EpisodeType.OpeningSong:
                displayTitle = $"C{episode.AniDB.EpisodeNumber} {displayTitle}";
                alternateTitle = $"C{episode.AniDB.EpisodeNumber} {alternateTitle}";
                break;
            case EpisodeType.Trailer:
                displayTitle = $"T{episode.AniDB.EpisodeNumber} {displayTitle}";
                alternateTitle = $"T{episode.AniDB.EpisodeNumber} {alternateTitle}";
                break;
            case EpisodeType.Parody:
                displayTitle = $"P{episode.AniDB.EpisodeNumber} {displayTitle}";
                alternateTitle = $"P{episode.AniDB.EpisodeNumber} {alternateTitle}";
                break;
            default:
                displayTitle = $"U{episode.AniDB.EpisodeNumber} {displayTitle}";
                alternateTitle = $"U{episode.AniDB.EpisodeNumber} {alternateTitle}";
                break;
        }

        var episodeNumber = Ordering.GetEpisodeNumber(group, series, episode);
        var seasonNumber = Ordering.GetSeasonNumber(group, series, episode);
        var (airsBeforeEpisodeNumber, airsBeforeSeasonNumber, airsAfterSeasonNumber, isSpecial) = Ordering.GetSpecialPlacement(group, series, episode);

        Episode result;
        if (season != null) {
            result = new Episode {
                Name = displayTitle,
                OriginalTitle = alternateTitle,
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
                CommunityRating = episode.AniDB.Rating.Value > 0 ? episode.AniDB.Rating.ToFloat(10) : 0,
                PremiereDate = episode.AniDB.AirDate,
                SeriesName = season.Series.Name,
                SeriesPresentationUniqueKey = season.SeriesPresentationUniqueKey,
                SeasonName = season.Name,
                ProductionLocations = TagFilter.GetSeasonContentRating(series).ToArray(),
                OfficialRating = ContentRating.GetSeasonContentRating(series),
                DateLastSaved = DateTime.UtcNow,
                RunTimeTicks = episode.AniDB.Duration.Ticks,
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
                PremiereDate = episode.AniDB.AirDate,
                Overview = description,
                OfficialRating = ContentRating.GetSeasonContentRating(series),
                CustomRating = group.CustomRating,
                CommunityRating = episode.AniDB.Rating.Value > 0 ? episode.AniDB.Rating.ToFloat(10) : 0,
            };
        }

        if (file != null && file.EpisodeList.Count > 1) {
            var episodeNumberEnd = episodeNumber + file.EpisodeList.Count - 1;
            if (episodeNumberEnd != episodeNumber && episode.AniDB.EpisodeNumber != episodeNumberEnd)
                result.IndexNumberEnd = episodeNumberEnd;
        }

        AddProviderIds(result, episodeId: episode.Id, fileId: file?.Id, seriesId: file?.SeriesId, anidbId: episode.AniDB.Id.ToString());

        return result;
    }

    private static void AddProviderIds(IHasProviderIds item, string episodeId, string? fileId = null, string? seriesId = null, string? anidbId = null, string? tmdbId = null)
    {
        var config = Plugin.Instance.Configuration;
        item.SetProviderId(ShokoEpisodeId.Name, episodeId);
        if (!string.IsNullOrEmpty(fileId))
            item.SetProviderId(ShokoFileId.Name, fileId);
        if (!string.IsNullOrEmpty(seriesId))
            item.SetProviderId(ShokoSeriesId.Name, seriesId);
        if (config.AddAniDBId && !string.IsNullOrEmpty(anidbId) && anidbId != "0")
            item.SetProviderId("AniDB", anidbId);
        if (config.AddTMDBId &&!string.IsNullOrEmpty(tmdbId) && tmdbId != "0")
            item.SetProviderId(MetadataProvider.Tmdb, tmdbId);
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        => Task.FromResult<IEnumerable<RemoteSearchResult>>([]);

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => HttpClientFactory.CreateClient().GetAsync(url, cancellationToken);
}
