using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Shokofin.API;
using Shokofin.ExternalIds;
using Shokofin.Utils;

using Info = Shokofin.API.Info;

namespace Shokofin.Providers;

public class SeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>, ICustomMetadataProvider<Series>
{
    public string Name => Plugin.MetadataProviderName;

    private readonly IHttpClientFactory HttpClientFactory;

    private readonly ILogger<SeriesProvider> Logger;

    private readonly ShokoAPIManager ApiManager;

    private readonly IFileSystem FileSystem;

    private readonly IIdLookup Lookup;

    private readonly ILibraryManager LibraryManager;

    private readonly ILocalizationManager LocalizationManager;

    public SeriesProvider(IHttpClientFactory httpClientFactory, ILogger<SeriesProvider> logger, ShokoAPIManager apiManager, IFileSystem fileSystem, IIdLookup lookup, ILibraryManager libraryManager, ILocalizationManager localizationManager)
    {
        Logger = logger;
        HttpClientFactory = httpClientFactory;
        ApiManager = apiManager;
        FileSystem = fileSystem;
        Lookup = lookup;
        LibraryManager = libraryManager;
        LocalizationManager = localizationManager;
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

            var ( displayTitle, alternateTitle ) = Text.GetSeriesTitles(show.DefaultSeason.AniDB.Titles, show.Name, info.MetadataLanguage);
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
                Tags = show.Tags,
                Genres = show.Genres,
                Studios = show.Studios,
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
        // NOTE: These next line will remain here till _someone_ fix the series merging for providers other then TvDB and ImDB in Jellyfin.
        // NOTE: #2 Will fix this once JF 10.9 is out, as it contains a change that will help in this situation.
        item.SetProviderId(MetadataProvider.Imdb, $"INVALID-BUT-DO-NOT-TOUCH:{seriesId}");

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

    public async Task<ItemUpdateType> FetchAsync(Series series, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        // Abort if we're unable to get the shoko series id
        if (!Lookup.TryGetSeriesIdFor(series, out var seriesId))
            return ItemUpdateType.None;

        // Provide metadata for a series using Shoko's Group feature
        var showInfo = await ApiManager.GetShowInfoForSeries(seriesId);
        if (showInfo == null || showInfo.SeasonList.Count == 0) {
            Logger.LogWarning("Unable to find show info for series. (Series={SeriesID})", seriesId);
            return ItemUpdateType.None;
        }

        // Get the existing seasons and episode ids
        var itemUpdated = ItemUpdateType.None;
        if (Plugin.Instance.Configuration.AddMissingMetadata) {
            var hasSpecials = false;
            var (seasons, _) = GetExistingSeasonsAndEpisodeIds(series);
            foreach (var pair in showInfo.SeasonOrderDictionary) {
                if (seasons.ContainsKey(pair.Key))
                    continue;
                if (pair.Value.SpecialsList.Count > 0)
                    hasSpecials = true;
                var offset = pair.Key - showInfo.SeasonNumberBaseDictionary[pair.Value.Id];
                var season = AddVirtualSeason(pair.Value, offset, pair.Key, series);
                if (season != null)
                    itemUpdated |= ItemUpdateType.MetadataImport;
            }

            if (hasSpecials && !seasons.ContainsKey(0)) {
                var season = AddVirtualSeason(0, series);
                if (season != null)
                    itemUpdated |= ItemUpdateType.MetadataImport;
            }
        }

        return itemUpdated;
    }
    private (Dictionary<int, Season>, HashSet<string>) GetExistingSeasonsAndEpisodeIds(Series series)
    {
        var seasons = new Dictionary<int, Season>();
        var episodes = new HashSet<string>();
        foreach (var item in series.GetRecursiveChildren()) switch (item) {
            case Season season:
                if (season.IndexNumber.HasValue)
                    seasons.TryAdd(season.IndexNumber.Value, season);
                // Add all known episode ids for the season.
                if (Lookup.TryGetSeriesIdFor(season, out var seriesId))
                    foreach (var episodeId in ApiManager.GetLocalEpisodeIdsForSeries(seriesId))
                        episodes.Add(episodeId);
                break;
            case Episode episode:
                // Get a hash-set of existing episodes – both physical and virtual – to exclude when adding new virtual episodes.
                if (Lookup.TryGetEpisodeIdsFor(episode, out var episodeIds))
                    foreach (var episodeId in episodeIds)
                        episodes.Add(episodeId);
                else if (Lookup.TryGetEpisodeIdFor(episode, out var episodeId))
                    episodes.Add(episodeId);
                break;
        }
        return (seasons, episodes);
    }

    private bool SeasonExists(string seriesPresentationUniqueKey, string seriesName, int seasonNumber)
    {
        var searchList = LibraryManager.GetItemList(new() {
            IncludeItemTypes = new [] { Jellyfin.Data.Enums.BaseItemKind.Season },
            IndexNumber = seasonNumber,
            SeriesPresentationUniqueKey = seriesPresentationUniqueKey,
            DtoOptions = new(true),
        }, true);

        if (searchList.Count > 0) {
            Logger.LogDebug("Season {SeasonName} for Series {SeriesName} was created in another concurrent thread, skipping.", searchList[0].Name, seriesName);
            return true;
        }

        return false;
    }

    private Season? AddVirtualSeason(int seasonNumber, Series series)
    {
        if (SeasonExists(series.GetPresentationUniqueKey(), series.Name, seasonNumber))
            return null;

        string seasonName;
        if (seasonNumber == 0)
            seasonName = LibraryManager.GetLibraryOptions(series).SeasonZeroDisplayName;
        else
            seasonName = string.Format(LocalizationManager.GetLocalizedString("NameSeasonNumber"), seasonNumber.ToString(CultureInfo.InvariantCulture));

        var season = new Season {
            Name = seasonName,
            IndexNumber = seasonNumber,
            SortName = seasonName,
            ForcedSortName = seasonName,
            Id = LibraryManager.GetNewItemId(
                series.Id + "Season " + seasonNumber.ToString(CultureInfo.InvariantCulture),
                typeof(Season)),
            IsVirtualItem = true,
            SeriesId = series.Id,
            SeriesName = series.Name,
            SeriesPresentationUniqueKey = series.GetPresentationUniqueKey(),
            DateModified = DateTime.UtcNow,
            DateLastSaved = DateTime.UtcNow,
        };

        Logger.LogInformation("Adding virtual Season {SeasonNumber:00} to Series {SeriesName}.", seasonNumber, series.Name);

        series.AddChild(season);

        return season;
    }

    private Season? AddVirtualSeason(Info.SeasonInfo seasonInfo, int offset, int seasonNumber, Series series)
    {
        if (SeasonExists(series.GetPresentationUniqueKey(), series.Name, seasonNumber))
            return null;

        var seasonId = LibraryManager.GetNewItemId(series.Id + "Season " + seasonNumber.ToString(CultureInfo.InvariantCulture), typeof(Season));
        var season = SeasonProvider.CreateMetadata(seasonInfo, seasonNumber, offset, series, seasonId);

        Logger.LogInformation("Adding virtual Season {SeasonNumber:00} to Series {SeriesName}. (Series={SeriesId})", seasonNumber, series.Name, seasonInfo.Id);

        series.AddChild(season);

        return season;
    }

}
