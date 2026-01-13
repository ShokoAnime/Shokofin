using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;
using Shokofin.API.Info.AniDB;
using Shokofin.API.Info.Shoko;
using Shokofin.API.Info.TMDB;
using Shokofin.API.Models;
using Shokofin.API.Models.Shoko;
using Shokofin.API.Models.TMDB;
using Shokofin.Events.Interfaces;
using Shokofin.ExternalIds;
using Shokofin.Utils;

using ContentRating = Shokofin.API.Models.ContentRating;
using ContentRatingUtil = Shokofin.Utils.ContentRating;

namespace Shokofin.API.Info;

public class ShowInfo : IExtendedItemInfo {
    private readonly ShokoApiClient _client;

    public string Id { get; init; }

    public string InternalId => ShokoInternalId.SeriesNamespace + Id;

    /// <summary>
    /// Shoko Group Id used for Collection Support.
    /// </summary>
    public string? CollectionId { get; init; }

    public string Title { get; init; }

    public IReadOnlyList<Title> Titles { get; init; }

    public string? Overview { get; init; }

    public IReadOnlyList<Text> Overviews { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = [];

    public string? OriginalLanguageCode { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime LastUpdatedAt { get; init; }

    /// <summary>
    /// Indicates that this show is consistent of only movies.
    /// </summary>
    public bool IsMovieCollection { get; init; }

    /// <summary>
    /// Indicates this is a standalone show without a group attached to it.
    /// </summary>
    public bool IsStandalone { get; init; }

    /// <summary>
    /// First premiere date of the show.
    /// </summary>
    public DateTime? PremiereDate { get; init; }

    /// <summary>
    /// Ended date of the show.
    /// </summary>
    public DateTime? EndDate { get; init; }

    /// <summary>
    /// Custom rating of the show.
    /// </summary>
    public string? CustomRating =>
        DefaultSeason.IsRestricted ? "XXX" : null;

    /// <summary>
    /// Overall community rating of the show.
    /// </summary>
    public float CommunityRating { get; init; }

    /// <summary>
    /// All tags from across all seasons.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; }

    /// <summary>
    /// All genres from across all seasons.
    /// </summary>
    public IReadOnlyList<string> Genres { get; init; }

    /// <summary>
    /// All production locations from across all seasons.
    /// </summary>
    public IReadOnlyDictionary<ProviderName, IReadOnlyList<string>> ProductionLocations { get; init; }

    public IReadOnlyList<ContentRating> ContentRatings { get; init; }

    /// <summary>
    /// All studios from across all seasons.
    /// </summary>
    public IReadOnlyList<string> Studios { get; init; }

    /// <summary>
    /// The inferred days of the week this series airs on.
    /// </summary>
    /// <value>Each weekday</value>
    public IReadOnlyList<DayOfWeek> DaysOfWeek { get; init; }

    /// <summary>
    /// The yearly seasons this series belongs to.
    /// </summary>
    public IReadOnlyList<YearlySeason> YearlySeasons { get; init; }

    /// <summary>
    /// All staff from across all seasons.
    /// </summary>
    public IReadOnlyList<PersonInfo> Staff { get; init; }

    /// <summary>
    /// All seasons.
    /// </summary>
    public IReadOnlyList<SeasonInfo> SeasonList { get; init; }

    /// <summary>
    /// The season order dictionary.
    /// </summary>
    public IReadOnlyDictionary<int, SeasonInfo> SeasonOrderDictionary { get; init; }

    /// <summary>
    /// A pre-filtered set of special episode ids without an ExtraType
    /// attached.
    /// </summary>
    public IReadOnlyDictionary<string, bool> SpecialsDict { get; init; }

    /// <summary>
    /// The season number base-number dictionary.
    /// </summary>
    private Dictionary<string, int> SeasonNumberBaseDictionary { get; init; }

    /// <summary>
    /// Indicates that the show has specials.
    /// </summary>
    public bool HasSpecials =>
        SpecialsDict.Count > 0;

    /// <summary>
    /// Indicates that the show has specials with files.
    /// </summary>
    public bool HasSpecialsWithFiles =>
        SpecialsDict.Values.Contains(true);

    private bool? _isAvailable = null;

    public bool IsAvailable => _isAvailable ??= SeasonOrderDictionary.Values.Any(sI => sI.IsAvailable) || HasSpecialsWithFiles;

    /// <summary>
    /// The default season for the show.
    /// </summary>
    public readonly SeasonInfo DefaultSeason;

    /// <summary>
    /// Episode number padding for file name generation.
    /// </summary>
    public readonly int EpisodePadding;

    #region Shoko Series Metadata

    /// <summary>
    /// Main Shoko Series Id.
    /// </summary>
    public string? ShokoSeriesId => ShokoSeries?.FirstOrDefault()?.ShokoSeriesId;

    /// <summary>
    /// Main Shoko Group Id.
    /// </summary>
    public string? ShokoGroupId => ShokoSeries?.FirstOrDefault()?.ShokoGroupId;

    /// <summary>
    /// All Shoko series linked to the show info.
    /// </summary>
    public ShokoSeriesInfo[] ShokoSeries { get; init; }

    #endregion

    #region AniDB Anime Metadata

    /// <summary>
    /// Main AniDB Anime Id.
    /// </summary>
    public string? AnidbAnimeId => DefaultSeason.StructureType is not Configuration.SeriesStructureType.TMDB_SeriesAndMovies ? AnidbAnime.FirstOrDefault()?.AnidbAnimeId : null;

    /// <summary>
    /// All AniDB anime linked to the show info.
    /// </summary>
    public AnidbAnimeInfo[] AnidbAnime { get; init; }

    #endregion

    #region TMDB Show Metadata

    /// <summary>
    /// Main TMDB Show Id.
    /// </summary>
    public string? TmdbShowId => TmdbShows.FirstOrDefault()?.TmdbShowId;

    /// <summary>
    /// Main TvDB Show Id.
    /// </summary>
    public string? TvdbShowId => TmdbShows.FirstOrDefault()?.TvdbShowId;

    /// <summary>
    /// All TMDB shows linked to the show info.
    /// </summary>
    public TmdbShowInfo[] TmdbShows { get; init; }

    #endregion

    #region TMDB Movie Metadata

    /// <summary>
    /// Main TMDB Movie Collection Id.
    /// </summary>
    public string? TmdbMovieCollectionId => TmdbMovies.FirstOrDefault()?.TmdbMovieCollectionId;

    /// <summary>
    /// All TMDB movies linked to the show info.
    /// </summary>
    public TmdbMovieInfo[] TmdbMovies { get; init; }

    #endregion

    public ShowInfo(ShokoApiClient client, SeasonInfo seasonInfo, TmdbShow? tmdbShow = null, string? collectionId = null) {
        var seasonNumberBaseDictionary = new Dictionary<string, int>();
        var seasonOrderDictionary = new Dictionary<int, SeasonInfo>();
        var seasonNumberOffset = 1;
        if (seasonInfo.EpisodeList.Count > 0 || seasonInfo.AlternateEpisodesList.Count > 0)
            seasonNumberBaseDictionary.Add(seasonInfo.Id, seasonNumberOffset);
        if (seasonInfo.EpisodeList.Count > 0)
            seasonOrderDictionary.Add(seasonNumberOffset++, seasonInfo);
        if (seasonInfo.AlternateEpisodesList.Count > 0)
            seasonOrderDictionary.Add(seasonNumberOffset++, seasonInfo);

        _client = client;
        Id = seasonInfo.Id;
        CollectionId = collectionId ?? seasonInfo.ShokoGroupId!;
        IsMovieCollection = seasonInfo.Type is SeriesType.Movie;
        IsStandalone = true;
        Title = seasonInfo.Title;
        Overview = seasonInfo.Overview;
        if (tmdbShow != null) {
            Titles = seasonInfo.Titles.Where(t => t.Source is not "TMDB").Concat(tmdbShow.Titles).ToList();
            Overviews = seasonInfo.Overviews.Where(t => t.Source is not "TMDB").Concat(tmdbShow.Overviews).ToList();
        }
        else {
            Titles = seasonInfo.Titles;
            Overviews = seasonInfo.Overviews;
        }
        Notes = seasonInfo.Notes;
        OriginalLanguageCode = seasonInfo.OriginalLanguageCode;
        CommunityRating = seasonInfo.CommunityRating.ToFloat(10);
        Tags = seasonInfo.Tags;
        Genres = seasonInfo.Genres;
        PremiereDate = seasonInfo.PremiereDate;
        EndDate = seasonInfo.EndDate;
        CreatedAt = seasonInfo.CreatedAt;
        LastUpdatedAt = seasonInfo.LastUpdatedAt;
        ProductionLocations = seasonInfo.ProductionLocations;
        ContentRatings = seasonInfo.ContentRatings;
        Studios = seasonInfo.Studios;
        DaysOfWeek = seasonInfo.DaysOfWeek;
        YearlySeasons = seasonInfo.YearlySeasons;
        Staff = seasonInfo.Staff;
        SeasonList = [seasonInfo];
        SeasonNumberBaseDictionary = seasonNumberBaseDictionary;
        SeasonOrderDictionary = seasonOrderDictionary;
        SpecialsDict = seasonInfo.SpecialsList.ToDictionary(episodeInfo => episodeInfo.Id, episodeInfo => episodeInfo.IsAvailable);
        DefaultSeason = seasonInfo;
        EpisodePadding = Math.Max(2, (new int[] { seasonInfo.EpisodeList.Count, seasonInfo.AlternateEpisodesList.Count, seasonInfo.SpecialsList.Count }).Max().ToString().Length);
        AnidbAnime = seasonInfo.AnidbAnime;
        ShokoSeries = seasonInfo.ShokoSeries;
        TmdbShows = [..seasonInfo.TmdbSeasons.Select(tmdbSeason => tmdbSeason.ToShowInfo()).Distinct()];
        TmdbMovies = seasonInfo.TmdbMovies;
    }

    public ShowInfo(
        ShokoApiClient client,
        ILogger logger,
        ShokoGroup group,
        List<SeasonInfo> seasonList,
        ITmdbEntity? tmdbEntity,
        bool useGroupIdForCollection
    ) {
        // Order series list based on the main series or first available series.
        // Select the targeted id if a group specify a default series.
        var foundIndex = -1;
        var targetId = group.IDs.MainSeries.ToString();
        var orderingSeason = seasonList.FirstOrDefault(s => s.Id == targetId) ?? seasonList[0];
        switch (orderingSeason.SeasonOrdering) {
            case Ordering.OrderType.Default:
                foundIndex = seasonList.FindIndex(s => s.Id == targetId);
                break;
            case Ordering.OrderType.ReleaseDate:
                seasonList = [.. seasonList.OrderBy(s => s?.PremiereDate ?? DateTime.MaxValue)];
                foundIndex = 0;
                break;
            case Ordering.OrderType.Chronological:
            case Ordering.OrderType.ChronologicalIgnoreIndirect:
                seasonList.Sort(new SeriesInfoRelationComparer(orderingSeason.SeasonOrdering is Ordering.OrderType.Chronological));
                foundIndex = seasonList.FindIndex(s => s.Id == targetId);
                break;
        }

        // Fallback to the first series if we can't get a base point for seasons.
        var groupId = group.Id;
        if (foundIndex == -1) {
            logger.LogWarning("Unable to get a base-point for seasons within the group for the filter, so falling back to the first series in the group. This is most likely due to library separation being enabled. (Group={GroupID})", groupId);
            foundIndex = 0;
        }

        var defaultSeason = seasonList[foundIndex];
        var specialsSet = new Dictionary<string, bool>();
        var seasonOrderDictionary = new Dictionary<int, SeasonInfo>();
        var seasonNumberBaseDictionary = new Dictionary<string, int>();
        var seasonNumberOffset = 1;
        foreach (var seasonInfo in seasonList) {
            if (seasonInfo.EpisodeList.Count > 0 || seasonInfo.AlternateEpisodesList.Count > 0)
                seasonNumberBaseDictionary.Add(seasonInfo.Id, seasonNumberOffset);
            if (seasonInfo.EpisodeList.Count > 0)
                seasonOrderDictionary.Add(seasonNumberOffset++, seasonInfo);
            if (seasonInfo.AlternateEpisodesList.Count > 0)
                seasonOrderDictionary.Add(seasonNumberOffset++, seasonInfo);
            foreach (var episodeInfo in seasonInfo.SpecialsList)
                specialsSet.Add(episodeInfo.Id, episodeInfo.IsAvailable);
        }

        var communityRatingSeasons = seasonOrderDictionary
            .Where(pair => seasonNumberBaseDictionary.TryGetValue(pair.Value.Id, out var seasonNumber) && seasonNumber == pair.Key && pair.Value.CommunityRating is { Value: > 0 })
            .Select(pair => pair.Value)
            .ToList();
        var anidbRating = ContentRatingUtil.GetCombinedAnidbContentRating(seasonOrderDictionary.Values);
        var contentRatings = seasonOrderDictionary.Values
            .SelectMany(sI => sI.ContentRatings)
            .Where(cR => cR.Source is not "AniDB")
            .Distinct()
            .ToList();
        if (!string.IsNullOrEmpty(anidbRating))
            contentRatings.Add(new() {
                Rating = anidbRating,
                Country = "US",
                Language = "en",
                Source = "AniDB",
            });

        _client = client;
        Id = defaultSeason.Id;
        Title = group.Name;
        Titles = [
            ..defaultSeason.Titles.Where(t => t.Source is "AniDB"),
            ..(tmdbEntity?.Titles ?? []),
        ];
        Overview = !group.HasCustomDescription
            ? TextUtility.SanitizeAnidbDescription(group.Description)
            : group.Description;
        Overviews = [
            ..defaultSeason.Overviews.Where(t => t.Source is "AniDB"),
            ..(tmdbEntity?.Overviews ?? []),
        ];
        Notes = defaultSeason.Notes;
        CollectionId = useGroupIdForCollection ? groupId : group.IDs.ParentGroup?.ToString();
        IsStandalone = false;
        PremiereDate = seasonList.Select(s => s.PremiereDate).Where(s => s.HasValue).Min();
        EndDate = !seasonList.Any(s => s.PremiereDate.HasValue && s.PremiereDate.Value < DateTime.Now && s.EndDate == null)
            ? seasonList.Select(s => s.EndDate).Where(s => s.HasValue).Max()
            : null;
        CreatedAt = seasonList.Select(s => s.CreatedAt).Min();
        LastUpdatedAt = seasonList.Select(s => s.LastUpdatedAt).Max();
        CommunityRating = communityRatingSeasons.Count > 0
            ? communityRatingSeasons.Aggregate(0f, (total, seasonInfo) => total + seasonInfo.CommunityRating.ToFloat(10)) / communityRatingSeasons.Count
            : 0f;
        Genres = seasonList.SelectMany(s => s.Genres).Distinct().ToArray();
        Tags = seasonList.SelectMany(s => s.Tags).Distinct().ToArray();
        Studios = seasonList.SelectMany(s => s.Studios).Distinct().ToArray();
        ProductionLocations = seasonList
            .SelectMany(sI => sI.ProductionLocations)
            .GroupBy(kP => kP.Key, kP => kP.Value)
            .ToDictionary(gB => gB.Key, gB => gB.SelectMany(l => l).Distinct().ToList() as IReadOnlyList<string>);
        ContentRatings = contentRatings;
        DaysOfWeek = seasonList[^1].DaysOfWeek;
        YearlySeasons = seasonList.SelectMany(s => s.YearlySeasons).Distinct().Order().ToArray();
        Staff = seasonList.SelectMany(s => s.Staff).DistinctBy(p => new { p.Type, p.Name, p.Role }).ToArray();
        SeasonList = seasonList;
        SeasonNumberBaseDictionary = seasonNumberBaseDictionary;
        SeasonOrderDictionary = seasonOrderDictionary;
        SpecialsDict = specialsSet;
        DefaultSeason = defaultSeason;
        EpisodePadding = Math.Max(2, seasonList.SelectMany(s => new int[] { s.EpisodeList.Count, s.AlternateEpisodesList.Count }).Append(specialsSet.Count).Max().ToString().Length);
        AnidbAnime = [..defaultSeason.AnidbAnime.Concat(seasonList.Except([defaultSeason]).SelectMany(s => s.AnidbAnime)).Distinct()];
        ShokoSeries = [..defaultSeason.ShokoSeries.Concat(seasonList.Except([defaultSeason]).SelectMany(s => s.ShokoSeries)).Distinct()];
        TmdbShows = [
            ..(tmdbEntity is TmdbShow tmdbShow ? (
                new TmdbShowInfo[] { tmdbShow.ToInfo() }
                    .Concat(seasonList.SelectMany(s => s.TmdbSeasons.Select(tmdbSeason => tmdbSeason.ToShowInfo())))
                    .Distinct()
            ) : (
              seasonList.SelectMany(s => s.TmdbSeasons.Select(tmdbSeason => tmdbSeason.ToShowInfo())).Distinct()
            )),
        ];
        TmdbMovies = [..seasonList.SelectMany(s => s.TmdbMovies).Distinct()];
    }

    public ShowInfo(ShokoApiClient client, TmdbShow tmdbShow, IReadOnlyList<SeasonInfo> seasonList) {
        var defaultSeason = seasonList[0];
        var specialsSet = new Dictionary<string, bool>();
        var seasonOrderDictionary = new Dictionary<int, SeasonInfo>();
        var seasonNumberBaseDictionary = new Dictionary<string, int>();
        var seasonNumberOffset = 1;
        foreach (var seasonInfo in seasonList) {
            if (seasonInfo.EpisodeList.Count > 0 || seasonInfo.AlternateEpisodesList.Count > 0)
                seasonNumberBaseDictionary.Add(seasonInfo.Id, seasonNumberOffset);
            if (seasonInfo.EpisodeList.Count > 0)
                seasonOrderDictionary.Add(seasonNumberOffset++, seasonInfo);
            if (seasonInfo.AlternateEpisodesList.Count > 0)
                seasonOrderDictionary.Add(seasonNumberOffset++, seasonInfo);
            foreach (var episodeInfo in seasonInfo.SpecialsList)
                specialsSet.Add(episodeInfo.Id, episodeInfo.IsAvailable);
        }

        _client = client;
        Id = defaultSeason.Id;
        if (seasonList.All(seasonInfo => seasonInfo.ShokoSeries.Length is > 0)) {
            var shokoGroupIdList = seasonList
                .SelectMany(s => s.ShokoSeries)
                .GroupBy(s => s.ShokoGroupId)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToList();
            if (shokoGroupIdList.Count is 1)
                CollectionId = shokoGroupIdList[0];
        }
        else if (seasonList.All(seasonInfo => !string.IsNullOrEmpty(seasonInfo.TopLevelShokoGroupId))) {
            var shokoGroupIdList = seasonList
                .GroupBy(s => s.TopLevelShokoGroupId)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToList();
            if (shokoGroupIdList.Count is 1)
                CollectionId = shokoGroupIdList[0];
        }
        IsMovieCollection = false;
        IsStandalone = true;
        Title = tmdbShow.Title;
        Titles = tmdbShow.Titles;
        Overview = tmdbShow.Overview;
        Overviews = tmdbShow.Overviews;
        OriginalLanguageCode = tmdbShow.OriginalLanguage;
        PremiereDate = tmdbShow.FirstAiredAt?.ToDateTime(TimeOnly.Parse("00:00:00", CultureInfo.InvariantCulture), DateTimeKind.Local);
        EndDate = tmdbShow.LastAiredAt?.ToDateTime(TimeOnly.Parse("00:00:00", CultureInfo.InvariantCulture), DateTimeKind.Local);
        CreatedAt = tmdbShow.CreatedAt;
        LastUpdatedAt = tmdbShow.LastUpdatedAt;
        CommunityRating = tmdbShow.UserRating.ToFloat(10);
        Genres = seasonList.SelectMany(s => s.Genres).Distinct().ToArray();
        Tags = seasonList.SelectMany(s => s.Tags).Distinct().ToArray();
        Studios = seasonList.SelectMany(s => s.Studios).Distinct().ToArray();
        ProductionLocations = seasonList
            .SelectMany(sI => sI.ProductionLocations)
            .GroupBy(kP => kP.Key, kP => kP.Value)
            .ToDictionary(gB => gB.Key, gB => gB.SelectMany(l => l).Distinct().ToList() as IReadOnlyList<string>);
        ContentRatings = seasonList
            .SelectMany(sI => sI.ContentRatings)
            .Distinct()
            .ToList();
        DaysOfWeek = seasonList[^1].DaysOfWeek;
        YearlySeasons = seasonList.SelectMany(s => s.YearlySeasons).Distinct().Order().ToArray();
        Staff = seasonList.SelectMany(s => s.Staff).DistinctBy(p => new { p.Type, p.Name, p.Role }).ToArray();
        SeasonList = seasonList;
        SeasonNumberBaseDictionary = seasonNumberBaseDictionary;
        SeasonOrderDictionary = seasonOrderDictionary;
        SpecialsDict = specialsSet;
        DefaultSeason = defaultSeason;
        EpisodePadding = Math.Max(2, seasonList.SelectMany(s => new int[] { s.EpisodeList.Count, s.AlternateEpisodesList.Count }).Append(specialsSet.Count).Max().ToString().Length);
        AnidbAnime = [..seasonList.SelectMany(s => s.AnidbAnime).Distinct()];
        ShokoSeries = [..seasonList.SelectMany(s => s.ShokoSeries).Distinct()];
        TmdbShows = [tmdbShow.ToInfo()];
        TmdbMovies = [];
    }

    public ShowInfo(ShokoApiClient client, TmdbMovie tmdbMovie, SeasonInfo seasonInfo) {
        var releasedAt = tmdbMovie.ReleasedAt?.ToDateTime(TimeOnly.Parse("00:00:00", CultureInfo.InvariantCulture), DateTimeKind.Local);

        _client = client;
        Id = seasonInfo.Id;
        CollectionId = seasonInfo.ShokoGroupId ?? seasonInfo.TopLevelShokoGroupId;
        IsMovieCollection = true;
        IsStandalone = true;
        Title = tmdbMovie.Title;
        Titles = tmdbMovie.Titles;
        Overview = tmdbMovie.Overview;
        Overviews = tmdbMovie.Overviews;
        OriginalLanguageCode = tmdbMovie.OriginalLanguage;
        PremiereDate = releasedAt;
        EndDate = releasedAt < DateTime.Now ? releasedAt : null;
        CreatedAt = tmdbMovie.CreatedAt;
        LastUpdatedAt = tmdbMovie.LastUpdatedAt;
        CommunityRating = tmdbMovie.UserRating.ToFloat(10);
        Genres = seasonInfo.Genres;
        Tags = seasonInfo.Tags;
        Studios = seasonInfo.Studios;
        ProductionLocations = seasonInfo.ProductionLocations;
        ContentRatings = seasonInfo.ContentRatings;
        DaysOfWeek = [];
        YearlySeasons = seasonInfo.YearlySeasons;
        Staff = seasonInfo.Staff;
        SeasonList = [seasonInfo];
        SeasonNumberBaseDictionary = new Dictionary<string, int> { { seasonInfo.Id, 1 } };
        SeasonOrderDictionary = new Dictionary<int, SeasonInfo> { { 1, seasonInfo } };
        SpecialsDict = new Dictionary<string, bool>();
        DefaultSeason = seasonInfo;
        EpisodePadding = Math.Max(2, (new int[] { seasonInfo.EpisodeList.Count, seasonInfo.AlternateEpisodesList.Count, seasonInfo.SpecialsList.Count }).Max().ToString().Length);
        AnidbAnime = seasonInfo.AnidbAnime;
        ShokoSeries = seasonInfo.ShokoSeries;
        TmdbShows = [..seasonInfo.TmdbSeasons.Select(tmdbSeason => tmdbSeason.ToShowInfo()).Distinct()];
        TmdbMovies = seasonInfo.TmdbMovies;
    }

    public ShowInfo(ShokoApiClient client, TmdbMovieCollection tmdbMovieCollection, IReadOnlyList<SeasonInfo> seasonList) {
        var defaultSeason = seasonList[0];
        var specialsSet = new Dictionary<string, bool>();
        var seasonOrderDictionary = new Dictionary<int, SeasonInfo>();
        var seasonNumberBaseDictionary = new Dictionary<string, int>();
        var seasonNumberOffset = 1;
        foreach (var seasonInfo in seasonList) {
            if (seasonInfo.EpisodeList.Count > 0 || seasonInfo.AlternateEpisodesList.Count > 0)
                seasonNumberBaseDictionary.Add(seasonInfo.Id, seasonNumberOffset);
            if (seasonInfo.EpisodeList.Count > 0)
                seasonOrderDictionary.Add(seasonNumberOffset++, seasonInfo);
            if (seasonInfo.AlternateEpisodesList.Count > 0)
                seasonOrderDictionary.Add(seasonNumberOffset++, seasonInfo);
            foreach (var episodeInfo in seasonInfo.SpecialsList)
                specialsSet.Add(episodeInfo.Id, episodeInfo.IsAvailable);
        }
        var communityRatingSeasons = seasonOrderDictionary
            .Where(pair => seasonNumberBaseDictionary.TryGetValue(pair.Value.Id, out var seasonNumber) && seasonNumber == pair.Key && pair.Value.CommunityRating is { Value: > 0 })
            .Select(pair => pair.Value)
            .ToList();

        _client = client;
        Id = defaultSeason.Id;
        if (seasonList.All(seasonInfo => seasonInfo.ShokoSeries.Length is > 0)) {
            var shokoGroupIdList = seasonList
                .SelectMany(s => s.ShokoSeries)
                .GroupBy(s => s.ShokoGroupId)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToList();
            if (shokoGroupIdList.Count is 1)
                CollectionId = shokoGroupIdList[0];
        }
        else if (seasonList.All(seasonInfo => !string.IsNullOrEmpty(seasonInfo.TopLevelShokoGroupId))) {
            var shokoGroupIdList = seasonList
                .GroupBy(s => s.TopLevelShokoGroupId)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToList();
            if (shokoGroupIdList.Count is 1)
                CollectionId = shokoGroupIdList[0];
        }
        IsMovieCollection = seasonList.Count is 1;
        IsStandalone = false;
        Title = tmdbMovieCollection.Title;
        Titles = tmdbMovieCollection.Titles;
        Overview = tmdbMovieCollection.Overview;
        Overviews = tmdbMovieCollection.Overviews;
        OriginalLanguageCode = defaultSeason.OriginalLanguageCode;
        PremiereDate = seasonList[0].PremiereDate;
        EndDate = seasonList[^1].EndDate;
        CreatedAt = tmdbMovieCollection.CreatedAt;
        LastUpdatedAt = tmdbMovieCollection.LastUpdatedAt;
        CommunityRating = communityRatingSeasons.Count > 0
            ? communityRatingSeasons.Aggregate(0f, (total, seasonInfo) => total + seasonInfo.CommunityRating.ToFloat(10)) / communityRatingSeasons.Count
            : 0f;
        Tags = seasonList.SelectMany(s => s.Tags).Distinct().ToArray();
        Genres = seasonList.SelectMany(s => s.Genres).Distinct().ToArray();
        ProductionLocations = seasonList
            .SelectMany(sI => sI.ProductionLocations)
            .GroupBy(kP => kP.Key, kP => kP.Value)
            .ToDictionary(gB => gB.Key, gB => gB.SelectMany(l => l).Distinct().ToList() as IReadOnlyList<string>);
        ContentRatings = seasonList
            .SelectMany(sI => sI.ContentRatings)
            .Distinct()
            .ToList();
        Studios = seasonList.SelectMany(s => s.Studios).Distinct().ToArray();
        DaysOfWeek = [];
        YearlySeasons = seasonList.SelectMany(s => s.YearlySeasons).Distinct().Order().ToArray();
        Staff = seasonList.SelectMany(s => s.Staff).DistinctBy(p => new { p.Type, p.Name, p.Role }).ToArray();
        SeasonList = seasonList;
        SeasonNumberBaseDictionary = seasonNumberBaseDictionary;
        SeasonOrderDictionary = seasonOrderDictionary;
        SpecialsDict = specialsSet;
        DefaultSeason = defaultSeason;
        EpisodePadding = Math.Max(2, seasonList.SelectMany(s => new int[] { s.EpisodeList.Count, s.AlternateEpisodesList.Count }).Append(specialsSet.Count).Max().ToString().Length);
        AnidbAnime = [..seasonList.SelectMany(s => s.AnidbAnime).Distinct()];
        ShokoSeries = [..seasonList.SelectMany(s => s.ShokoSeries).Distinct()];
        TmdbShows = [];
        TmdbMovies = [..seasonList.SelectMany(s => s.TmdbMovies).Distinct()];
    }

    public async Task<Images> GetImages(CancellationToken cancellationToken)
        => Id[0] switch {
                IdPrefix.TmdbShow => await _client.GetImagesForTmdbShow(TmdbShowId!, cancellationToken).ConfigureAwait(false),
                IdPrefix.TmdbMovie => !string.IsNullOrEmpty(TmdbMovieCollectionId)
                    ? await _client.GetImagesForTmdbMovieCollection(TmdbMovieCollectionId, cancellationToken).ConfigureAwait(false)
                    : await _client.GetImagesForTmdbMovie(Id[1..], cancellationToken).ConfigureAwait(false),
                IdPrefix.TmdbMovieCollection => await _client.GetImagesForTmdbMovieCollection(Id[1..], cancellationToken).ConfigureAwait(false),
                _ => await _client.GetImagesForShokoSeries(Id, cancellationToken).ConfigureAwait(false),
            } ?? new();

    public bool IsSpecial(EpisodeInfo episodeInfo)
        => SpecialsDict.ContainsKey(episodeInfo.Id);

    public bool TryGetBaseSeasonNumberForSeasonInfo(SeasonInfo season, out int baseSeasonNumber)
        => SeasonNumberBaseDictionary.TryGetValue(season.Id, out baseSeasonNumber);

    public int GetBaseSeasonNumberForSeasonInfo(SeasonInfo season)
        => SeasonNumberBaseDictionary.TryGetValue(season.Id, out var baseSeasonNumber) ? baseSeasonNumber : 0;

    public SeasonInfo? GetSeasonInfoBySeasonNumber(int seasonNumber)
        => seasonNumber is > 0 && SeasonOrderDictionary.TryGetValue(seasonNumber, out var seasonInfo) ? seasonInfo : null;
}
