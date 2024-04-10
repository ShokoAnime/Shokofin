using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.ExternalIds;
using Shokofin.Utils;

using Info = Shokofin.API.Info;

namespace Shokofin.Providers;

public class SeasonProvider : IRemoteMetadataProvider<Season, SeasonInfo>
{
    public string Name => Plugin.MetadataProviderName;

    private readonly IHttpClientFactory HttpClientFactory;

    private readonly ILogger<SeasonProvider> Logger;

    private readonly ShokoAPIManager ApiManager;

    private readonly IIdLookup Lookup;

    private readonly ILibraryManager LibraryManager;

    public SeasonProvider(IHttpClientFactory httpClientFactory, ILogger<SeasonProvider> logger, ShokoAPIManager apiManager, IIdLookup lookup, ILibraryManager libraryManager)
    {
        HttpClientFactory = httpClientFactory;
        Logger = logger;
        ApiManager = apiManager;
        Lookup = lookup;
        LibraryManager = libraryManager;
    }

    public async Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken cancellationToken)
    {
        try {
            var result = new MetadataResult<Season>();
            if (!info.IndexNumber.HasValue)
                return result;

            // Special handling of the "Specials" season (pun intended).
            if (info.IndexNumber.Value == 0) {
                // We're forcing the sort names to start with "ZZ" to make it 
                // always appear last in the UI.
                var seasonName = info.Name;
                result.Item = new Season {
                    Name = seasonName,
                    IndexNumber = info.IndexNumber,
                    SortName = $"ZZ - {seasonName}",
                    ForcedSortName = $"ZZ - {seasonName}",
                };
                result.HasMetadata = true;

                return result;
            }

            if (!info.SeriesProviderIds.TryGetValue(ShokoSeriesId.Name, out var seriesId) || !info.IndexNumber.HasValue) {
                Logger.LogDebug("Unable refresh Season {SeasonNumber} {SeasonName}", info.IndexNumber, info.Name);
                return result;
            }

            var seasonNumber = info.IndexNumber.Value;
            var showInfo = await ApiManager.GetShowInfoForSeries(seriesId);
            if (showInfo == null) {
                Logger.LogWarning("Unable to find show info for Season {SeasonNumber}. (Series={SeriesId})", seasonNumber, seriesId);
                return result;
            }

            var seasonInfo = showInfo.GetSeasonInfoBySeasonNumber(seasonNumber);
            if (seasonInfo == null || !showInfo.TryGetBaseSeasonNumberForSeasonInfo(seasonInfo, out var baseSeasonNumber)) {
                Logger.LogWarning("Unable to find series info for Season {SeasonNumber}. (Series={SeriesId},Group={GroupId})", seasonNumber, seriesId, showInfo.GroupId);
                return result;
            }

            Logger.LogInformation("Found info for Season {SeasonNumber} in Series {SeriesName} (Series={SeriesId},Group={GroupId})", seasonNumber, showInfo.Name, seriesId, showInfo.GroupId);

            var offset = Math.Abs(seasonNumber - baseSeasonNumber);

            result.Item = CreateMetadata(seasonInfo, seasonNumber, offset, info.MetadataLanguage);
            result.HasMetadata = true;
            result.ResetPeople();
            foreach (var person in seasonInfo.Staff)
                result.AddPerson(person);

            return result;
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Threw unexpectedly; {Message}", ex.Message);
            return new MetadataResult<Season>();
        }
    }

    public static Season CreateMetadata(Info.SeasonInfo seasonInfo, int seasonNumber, int offset, string metadataLanguage)
        => CreateMetadata(seasonInfo, seasonNumber, offset, metadataLanguage, null, Guid.Empty);

    public static Season CreateMetadata(Info.SeasonInfo seasonInfo, int seasonNumber, int offset, Series series, Guid seasonId)
        => CreateMetadata(seasonInfo, seasonNumber, offset, series.GetPreferredMetadataLanguage(), series, seasonId);

    public static Season CreateMetadata(Info.SeasonInfo seasonInfo, int seasonNumber, int offset, string metadataLanguage, Series? series, Guid seasonId)
    {
        var ( displayTitle, alternateTitle ) = Text.GetSeriesTitles(seasonInfo.AniDB.Titles, seasonInfo.Shoko.Name, metadataLanguage);
        var sortTitle = $"S{seasonNumber} - {seasonInfo.Shoko.Name}";

        if (offset > 0) {
            string type = string.Empty;
            switch (offset) {
                default:
                    break;
                case 1:
                    type = "Alternate Version";
                    break;
            }
            if (!string.IsNullOrEmpty(type)) {
                displayTitle += $" ({type})";
                alternateTitle += $" ({type})";
            }
        }

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
                Overview = Text.GetDescription(seasonInfo),
                PremiereDate = seasonInfo.AniDB.AirDate,
                EndDate = seasonInfo.AniDB.EndDate,
                ProductionYear = seasonInfo.AniDB.AirDate?.Year,
                Tags = seasonInfo.Tags.ToArray(),
                Genres = seasonInfo.Genres.ToArray(),
                Studios = seasonInfo.Studios.ToArray(),
                CommunityRating = seasonInfo.AniDB.Rating?.ToFloat(10),
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
                Overview = Text.GetDescription(seasonInfo),
                PremiereDate = seasonInfo.AniDB.AirDate,
                EndDate = seasonInfo.AniDB.EndDate,
                ProductionYear = seasonInfo.AniDB.AirDate?.Year,
                Tags = seasonInfo.Tags.ToArray(),
                Genres = seasonInfo.Genres.ToArray(),
                Studios = seasonInfo.Studios.ToArray(),
                CommunityRating = seasonInfo.AniDB.Rating?.ToFloat(10),
            };
        }
        season.ProviderIds.Add(ShokoSeriesId.Name, seasonInfo.Id);
        if (Plugin.Instance.Configuration.AddAniDBId)
            season.ProviderIds.Add("AniDB", seasonInfo.AniDB.Id.ToString());

        return season;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo searchInfo, CancellationToken cancellationToken)
        => Task.FromResult<IEnumerable<RemoteSearchResult>>(new List<RemoteSearchResult>());

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => HttpClientFactory.CreateClient().GetAsync(url, cancellationToken);
        
    public async Task<ItemUpdateType> FetchAsync(Season season, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        // We're not interested in the dummy season.
        if (!season.IndexNumber.HasValue)
            return ItemUpdateType.None;

        // Abort if we're unable to get the shoko series id
        var series = season.Series;
        if (!Lookup.TryGetSeriesIdFor(series, out var seriesId))
            return ItemUpdateType.None;

        var seasonNumber = season.IndexNumber!.Value;
        var showInfo = await ApiManager.GetShowInfoForSeries(seriesId);
        if (showInfo == null || showInfo.SeasonList.Count == 0) {
            Logger.LogWarning("Unable to find show info for season. (Series={SeriesId})", seriesId);
            return ItemUpdateType.None;
        }

        var itemUpdated = ItemUpdateType.None;
        if (Plugin.Instance.Configuration.AddMissingMetadata) {
            // Get a hash-set of existing episodes – both physical and virtual – to exclude when adding new virtual episodes.
            var existingEpisodes = new HashSet<string>();
            foreach (var episode in season.Children.OfType<Episode>()) {
                if (Lookup.TryGetEpisodeIdsFor(episode, out var episodeIds))
                    foreach (var episodeId in episodeIds)
                        existingEpisodes.Add(episodeId);
                else if (Lookup.TryGetEpisodeIdFor(episode, out var episodeId))
                    existingEpisodes.Add(episodeId);
            }

            // Special handling of specials (pun intended).
            if (seasonNumber == 0) {
                foreach (var sI in showInfo.SeasonList) {
                    foreach (var episodeId in ApiManager.GetLocalEpisodeIdsForSeries(sI.Id))
                        existingEpisodes.Add(episodeId);

                    foreach (var episodeInfo in sI.SpecialsList) {
                        if (existingEpisodes.Contains(episodeInfo.Id))
                            continue;

                        if (AddVirtualEpisode(showInfo, sI, episodeInfo, season))
                            itemUpdated |= ItemUpdateType.MetadataImport;
                    }
                }
            }
            // Every other "season".
            else {
                var seasonInfo = showInfo.GetSeasonInfoBySeasonNumber(seasonNumber);
                if (seasonInfo == null) {
                    Logger.LogWarning("Unable to find series info for Season {SeasonNumber:00} in group for series. (Group={GroupId})", seasonNumber, showInfo.GroupId);
                    return ItemUpdateType.None;
                }

                foreach (var episodeId in ApiManager.GetLocalEpisodeIdsForSeries(seasonInfo.Id))
                    existingEpisodes.Add(episodeId);

                foreach (var episodeInfo in seasonInfo.EpisodeList.Concat(seasonInfo.AlternateEpisodesList)) {
                    var episodeParentIndex = Ordering.GetSeasonNumber(showInfo, seasonInfo, episodeInfo);
                    if (episodeParentIndex != seasonNumber)
                        continue;

                    if (existingEpisodes.Contains(episodeInfo.Id))
                        continue;

                    if (AddVirtualEpisode(showInfo, seasonInfo, episodeInfo, season))
                        itemUpdated |= ItemUpdateType.MetadataImport;
                }
            }
        }

        // Remove the virtual season/episode that matches the newly updated item
        var searchList = LibraryManager
            .GetItemList(
                new() {
                    ParentId = season.ParentId,
                    IncludeItemTypes = new [] { Jellyfin.Data.Enums.BaseItemKind.Season },
                    ExcludeItemIds = new [] { season.Id },
                    IndexNumber = seasonNumber,
                    DtoOptions = new(true),
                },
                true
            )
            .Where(item => !item.IndexNumber.HasValue)
            .ToList();
        if (searchList.Count > 0)
        {
            Logger.LogInformation("Removing {Count:00} duplicate seasons from Series {SeriesName} (Series={SeriesId})", searchList.Count, series.Name, seriesId);

            var deleteOptions = new DeleteOptions { DeleteFileLocation = false };
            foreach (var item in searchList)
                LibraryManager.DeleteItem(item, deleteOptions);

            itemUpdated |= ItemUpdateType.MetadataEdit;
        }


        return itemUpdated;
    }

    private bool EpisodeExists(string episodeId, string seriesId, string? groupId)
    {
        var searchList = LibraryManager.GetItemList(new() {
            IncludeItemTypes = new [] { Jellyfin.Data.Enums.BaseItemKind.Episode },
            HasAnyProviderId = new Dictionary<string, string> { [ShokoEpisodeId.Name] = episodeId },
            DtoOptions = new(true),
        }, true);

        if (searchList.Count > 0) {
            Logger.LogDebug("A virtual or physical episode entry already exists for Episode {EpisodeName}. Ignoring. (Episode={EpisodeId},Series={SeriesId},Group={GroupId})", searchList[0].Name, episodeId, seriesId, groupId);
            return true;
        }
        return false;
    }

    private bool AddVirtualEpisode(Info.ShowInfo showInfo, Info.SeasonInfo seasonInfo, Info.EpisodeInfo episodeInfo, Season season)
    {
        if (EpisodeExists(episodeInfo.Id, seasonInfo.Id, showInfo.GroupId))
            return false;

        var episodeId = LibraryManager.GetNewItemId(season.Series.Id + " Season " + seasonInfo.Id + " Episode " + episodeInfo.Id, typeof(Episode));
        var episode = EpisodeProvider.CreateMetadata(showInfo, seasonInfo, episodeInfo, season, episodeId);

        Logger.LogInformation("Adding virtual Episode {EpisodeNumber:000} in Season {SeasonNumber:00} for Series {SeriesName}. (Episode={EpisodeId},Series={SeriesId},Group={GroupId})", episode.IndexNumber, season.Name, showInfo.Name, episodeInfo.Id, seasonInfo.Id, showInfo.GroupId);

        season.AddChild(episode);

        return true;
    }
}

