using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shokofin.API.Info.AniDB;
using Shokofin.API.Info.Shoko;
using Shokofin.API.Info.TMDB;
using Shokofin.API.Models;
using Shokofin.API.Models.Shoko;
using Shokofin.API.Models.TMDB;
using Shokofin.Configuration;
using Shokofin.Events.Interfaces;
using Shokofin.Extensions;
using Shokofin.ExternalIds;
using Shokofin.Utils;

using ContentRating = Shokofin.API.Models.ContentRating;
using PersonInfo = MediaBrowser.Controller.Entities.PersonInfo;

namespace Shokofin.API.Info;

public class SeasonInfo : IExtendedItemInfo {
    private readonly ShokoApiClient _client;

    public string Id { get; init; }

    public string InternalId => ShokoInternalId.SeriesNamespace + Id;

    public IReadOnlyList<string> ExtraIds { get; init; }

    public string? TopLevelShokoGroupId { get; init; }

    public SeriesType Type { get; init; }

    public SeriesStructureType StructureType { get; init; }

    public Ordering.OrderType SeasonOrdering { get; init; }

    public Ordering.SpecialOrderType SpecialsPlacement { get; init; }

    public bool IsMultiEntry { get; init; }

    public bool IsRestricted { get; init; }

    public string Title { get; init; }

    public IReadOnlyList<Title> Titles { get; init; }

    public string? Overview { get; init; }

    public IReadOnlyList<Text> Overviews { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = [];

    public string? OriginalLanguageCode { get; init; }

    public Rating CommunityRating { get; init; }

    /// <summary>
    /// First premiere date of the season.
    /// </summary>
    public DateTime? PremiereDate { get; init; }

    /// <summary>
    /// Ended date of the season.
    /// </summary>
    public DateTime? EndDate { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime LastUpdatedAt { get; init; }

    private bool? _isAvailable = null;

    public bool IsAvailable => _isAvailable ??= EpisodeList.Any(e => e.IsAvailable) || AlternateEpisodesList.Any(e => e.IsAvailable);

    public IReadOnlyList<string> Genres { get; init; }

    public IReadOnlyList<string> Tags { get; init; }

    public IReadOnlyList<string> Studios { get; init; }

    public IReadOnlyDictionary<ProviderName, IReadOnlyList<string>> ProductionLocations { get; init; }

    public IReadOnlyList<ContentRating> ContentRatings { get; init; }

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
    /// All staff for the season across all episodes.
    /// </summary>
    public IReadOnlyList<PersonInfo> Staff { get; init; }

    /// <summary>
    /// A pre-filtered list of normal episodes that belong to this series.
    ///
    /// Ordered by AniDb air-date.
    /// </summary>
    public IReadOnlyList<EpisodeInfo> EpisodeList { get; init; }

    /// <summary>
    /// A pre-filtered list of "unknown" episodes that belong to this series.
    ///
    /// Ordered by AniDb air-date.
    /// </summary>
    public IReadOnlyList<EpisodeInfo> AlternateEpisodesList { get; init; }

    /// <summary>
    /// A pre-filtered list of "extra" videos that belong to this series.
    ///
    /// Ordered by AniDb air-date.
    /// </summary>
    public IReadOnlyList<EpisodeInfo> ExtrasList { get; init; }

    /// <summary>
    /// A pre-filtered list of special episodes without an ExtraType
    /// attached.
    ///
    /// Ordered by AniDb episode number.
    /// </summary>
    public IReadOnlyList<EpisodeInfo> SpecialsList { get; init; }

    /// <summary>
    /// A list of special episodes that come before normal episodes.
    /// </summary>
    public IReadOnlySet<string> SpecialsBeforeEpisodes { get; init; }

    /// <summary>
    /// A dictionary holding mappings for the previous normal episode for every special episode in a series.
    /// </summary>
    public IReadOnlyDictionary<string, EpisodeInfo> SpecialsAnchors { get; init; }

    /// <summary>
    /// Related series data available in Shoko.
    /// </summary>
    public IReadOnlyList<Relation> Relations { get; init; }

    /// <summary>
    /// Map of related series with type.
    /// </summary>
    public IReadOnlyDictionary<string, RelationType> RelationMap { get; init; }

    #region Shoko Series Metadata

    /// <summary>
    /// The main Shoko series ID for the season info.
    /// </summary>
    public string? ShokoSeriesId => ShokoSeries.FirstOrDefault()?.ShokoSeriesId;

    /// <summary>
    /// The main Shoko group ID for the season info.
    /// </summary>
    public string? ShokoGroupId => ShokoSeries.FirstOrDefault()?.ShokoGroupId;

    /// <summary>
    /// All Shoko series linked to the season info.
    /// </summary>
    public ShokoSeriesInfo[] ShokoSeries { get; init; }

    #endregion
    
    #region AniDB Anime Metadata

    /// <summary>
    /// The main AniDB anime ID for the season info.
    /// </summary>
    public string? AnidbAnimeId => AnidbAnime.FirstOrDefault()?.AnidbAnimeId;

    /// <summary>
    /// All AniDB anime linked to the season info.
    /// </summary>
    public AnidbAnimeInfo[] AnidbAnime { get; init; }

    #endregion

    #region TMDB Season Metadata

    /// <summary>
    /// All TMDB seasons linked to the season info.
    /// </summary>
    public TmdbSeasonInfo[] TmdbSeasons { get; init; }

    #endregion

    #region TMDB Movie Metadata

    /// <summary>
    /// All TMDB movies linked to the season info.
    /// </summary>
    public TmdbMovieInfo[] TmdbMovies { get; init; }

    #endregion

    public SeasonInfo(
        ShokoApiClient client,
        ShokoSeries series,
        IEnumerable<string> extraIds,
        List<EpisodeInfo> episodes,
        IReadOnlyList<Relation> relations,
        ITmdbEntity? tmdbEntity,
        IReadOnlyDictionary<string, SeriesConfiguration> seriesConfigurationMap,
        TmdbSeasonInfo[] tmdbSeasons
    ) {
        var seasonId = series.Id;
        var relationMap = relations
            .Where(r => r.RelatedIDs.Shoko.HasValue)
            .DistinctBy(r => r.RelatedIDs.Shoko!.Value)
            .ToDictionary(r => r.RelatedIDs.Shoko!.Value.ToString(), r => r.Type);
        var specialsBeforeEpisodes = new HashSet<string>();
        var specialsAnchorDictionary = new Dictionary<string, EpisodeInfo>();
        var specialsList = new List<EpisodeInfo>();
        var episodesList = new List<EpisodeInfo>();
        var extrasList = new List<EpisodeInfo>();
        var altEpisodesList = new List<EpisodeInfo>();
        var seasonIdOrder = new string[] { seasonId }.Concat(extraIds).ToList();

        // Order the episodes by date.
        episodes = episodes
            .OrderBy(episode => !episode.AiredAt.HasValue)
            .ThenBy(episode => episode.AiredAt)
            .ThenBy(e => seasonIdOrder.IndexOf(e.SeasonId))
            .ThenBy(episode => episode.Type)
            .ThenBy(episode => episode.EpisodeNumber)
            .ToList();

        // Iterate over the episodes once and store some values for later use.
        int index = 0;
        int lastNormalEpisode = -1;
        foreach (var episode in episodes) {
            if (episode.IsHidden)
                continue;

            var seriesConfiguration = seriesConfigurationMap[episode.SeasonId];
            var episodeType = episode.Type is EpisodeType.Episode && seriesConfiguration.EpisodeConversion is SeriesEpisodeConversion.EpisodesAsSpecials ? EpisodeType.Special : episode.Type;
            switch (episodeType) {
                case EpisodeType.Episode:
                    episodesList.Add(episode);
                    lastNormalEpisode = index;
                    break;
                case EpisodeType.Other:
                    if (episode.ExtraType != null)
                        extrasList.Add(episode);
                    else
                        altEpisodesList.Add(episode);
                    break;
                default:
                    if (episode.ExtraType != null) {
                        extrasList.Add(episode);
                    }
                    else if (episodeType is EpisodeType.Special && seriesConfiguration.EpisodeConversion is SeriesEpisodeConversion.SpecialsAsEpisodes) {
                        episodesList.Add(episode);
                        lastNormalEpisode = index;
                    }
                    else if (episodeType is EpisodeType.Special) {
                        specialsList.Add(episode);
                        if (lastNormalEpisode == -1) {
                            specialsBeforeEpisodes.Add(episode.Id);
                        }
                        else {
                            var previousEpisode = episodes
                                .GetRange(lastNormalEpisode, index - lastNormalEpisode)
                                .FirstOrDefault(e => e.Type is EpisodeType.Episode && seriesConfiguration.EpisodeConversion is not SeriesEpisodeConversion.EpisodesAsSpecials);
                            if (previousEpisode != null)
                                specialsAnchorDictionary[episode.Id] = previousEpisode;
                        }
                    }
                    break;
            }
            index++;
        }

        // We order the lists after sorting them into buckets because the bucket
        // sort we're doing above have the episodes ordered by air date to get
        // the previous episode anchors right.
        if (!seriesConfigurationMap[seasonId].OrderByAirdate) {
            episodesList = episodesList
                .OrderBy(e => seasonIdOrder.IndexOf(e.SeasonId))
                .ThenBy(e => e.Type)
                .ThenBy(e => e.EpisodeNumber)
                .ToList();
            altEpisodesList = altEpisodesList
                .OrderBy(e => seasonIdOrder.IndexOf(e.SeasonId))
                .ThenBy(e => e.Type)
                .ThenBy(e => e.EpisodeNumber)
                .ToList();
            specialsList = specialsList
                .OrderBy(e => seasonIdOrder.IndexOf(e.SeasonId))
                .ThenBy(e => e.Type)
                .ThenBy(e => e.EpisodeNumber)
                .ToList();
        }

        // Replace the normal episodes if we've hidden all the normal episodes and we have at least one
        // alternate episode locally.
        var type = seriesConfigurationMap[seasonId].Type;
        var isCustomType = type != series.AniDB.Type;
        if (episodesList.Count == 0 && altEpisodesList.Count > 0) {
            // Switch the type from movie to web if we've hidden the main movie, and we have some of the parts.
            if (!isCustomType && type == SeriesType.Movie)
                type = SeriesType.Web;

            episodesList = altEpisodesList;
            altEpisodesList = [];

            // Re-create the special anchors because the episode list changed.
            index = 0;
            lastNormalEpisode = -1;
            specialsBeforeEpisodes.Clear();
            specialsAnchorDictionary.Clear();
            foreach (var episode in episodes) {
                if (episodesList.Contains(episode)) {
                    lastNormalEpisode = index;
                }
                else if (specialsList.Contains(episode)) {
                    if (lastNormalEpisode == -1) {
                        specialsBeforeEpisodes.Add(episode.Id);
                    }
                    else {
                        var previousEpisode = episodes
                            .GetRange(lastNormalEpisode, index - lastNormalEpisode)
                            .FirstOrDefault(e => episodesList.Contains(e));
                        if (previousEpisode != null)
                            specialsAnchorDictionary[episode.Id] = previousEpisode;
                    }
                }
                index++;
            }
        }
        // Also switch the type from movie to web if we're hidden the main movies, but the parts are normal episodes.
        else if (!isCustomType && type == SeriesType.Movie && episodes.Any(episodeInfo => episodeInfo.IsMainEntry && episodeInfo.IsHidden)) {
            type = SeriesType.Web;
        }

        if (seriesConfigurationMap[seasonId].EpisodeConversion is SeriesEpisodeConversion.SpecialsAsExtraFeaturettes) {
            if (specialsList.Count > 0) {
                extrasList.AddRange(specialsList);
                specialsAnchorDictionary.Clear();
                specialsList = [];
            }
            if (altEpisodesList.Count > 0) {
                extrasList.AddRange(altEpisodesList);
                altEpisodesList = [];
            }
        }

        var genres = episodes.SelectMany(s => s.Genres).ToList();
        var tags = episodes.SelectMany(s => s.Tags).ToList();
        AddYearlySeasons(ref genres, ref tags, series.YearlySeasons);

        _client = client;
        Id = seasonId;
        ExtraIds = extraIds.ToArray();
        TopLevelShokoGroupId = series.IDs.TopLevelGroup.ToString();
        StructureType = seriesConfigurationMap[seasonId].StructureType;
        SeasonOrdering = seriesConfigurationMap[seasonId].SeasonOrdering;
        SpecialsPlacement = seriesConfigurationMap[seasonId].SpecialsPlacement;
        Type = type;
        IsMultiEntry = type is SeriesType.Movie && series.Sizes.Total.Episodes > 1;
        IsRestricted = series.AniDB.Restricted;
        Title = series.Name;
        Titles = [
            ..series.AniDB.Titles,
            ..(tmdbEntity?.Titles ?? []),
        ];
        var notes = (IReadOnlyList<string>)[];
        Overview = series.Description == series.AniDB.Description
            ? TextUtility.SanitizeAnidbDescription(series.Description)
            : series.Description;
        Overviews = [
            ..(!string.IsNullOrEmpty(series.AniDB.Description) ? [
                new() {
                    IsDefault = true,
                    IsPreferred = string.Equals(series.Description, series.AniDB.Description),
                    LanguageCode = "en",
                    Source = "AniDB",
                    Value = TextUtility.SanitizeAnidbDescription(series.AniDB.Description, out notes),
                },
            ] : Array.Empty<Text>()),
            ..(tmdbEntity?.Overviews ?? []),
        ];
        Notes = notes;
        OriginalLanguageCode = null;
        CommunityRating = series.AniDB.Rating;
        PremiereDate = series.AniDB.AirDate;
        CreatedAt = series.CreatedAt;
        LastUpdatedAt = series.LastUpdatedAt;
        EndDate = series.AniDB.EndDate;
        Genres = genres.Distinct().Order().ToArray();
        Tags = tags.Distinct().Order().ToArray();
        Studios = episodes.SelectMany(s => s.Studios).Distinct().Order().ToArray();
        ProductionLocations = episodes
            .SelectMany(sI => sI.ProductionLocations)
            .GroupBy(kP => kP.Key, kP => kP.Value)
            .ToDictionary(gB => gB.Key, gB => gB.SelectMany(l => l).Distinct().Order().ToList() as IReadOnlyList<string>);
        ContentRatings = episodes
            .SelectMany(sI => sI.ContentRatings)
            .Distinct()
            .ToList();
        // Movies aren't aired on a schedule like series, so if even if the
        // movie series has it's type overridden then we don't want to attempt
        // to create a schedule.
        if (series.AniDB.Type is SeriesType.Movie) {
            DaysOfWeek = [];
        }
        else {
            DaysOfWeek = episodesList
                .Select(e => e.AiredAt)
                .WhereNotNullOrDefault()
                .Distinct()
                .Order()
                // A single cour season is usually 12-13 episodes, give or take.
                .TakeLast(12)
                // In case the two first episodes got an early screening in a 12-13 episode single cour anime.
                .Skip(2)
                .Select(e => e.DayOfWeek)
                .Distinct()
                .Order()
                .ToArray();
        }
        YearlySeasons = series.YearlySeasons;
        Staff = episodes.SelectMany(s => s.Staff).DistinctBy(p => new { p.Type, p.Name, p.Role }).ToArray();
        EpisodeList = episodesList;
        AlternateEpisodesList = altEpisodesList;
        ExtrasList = extrasList;
        SpecialsList = specialsList;
        SpecialsBeforeEpisodes = specialsBeforeEpisodes;
        SpecialsAnchors = specialsAnchorDictionary;
        Relations = relations;
        RelationMap = relationMap;
        ShokoSeries = [
            new() {
                ShokoSeriesId = series.Id,
                ShokoGroupId = series.IDs.ParentGroup.ToString(),
                TopLevelShokoGroupId = series.IDs.TopLevelGroup.ToString(),
            },
            ..extraIds.Select(extraId => new ShokoSeriesInfo {
                ShokoSeriesId = extraId,
                ShokoGroupId = series.IDs.ParentGroup.ToString(),
                TopLevelShokoGroupId = series.IDs.TopLevelGroup.ToString(),
            }),
        ];
        AnidbAnime = [new() {
            AnidbAnimeId = series.AniDB.Id.ToString(),
        }];
        TmdbSeasons = tmdbSeasons ?? [];
        TmdbMovies = [..EpisodeList.SelectMany(eI => eI.TmdbMovies).Distinct()];
    }

    public SeasonInfo(ShokoApiClient client, TmdbSeason tmdbSeason, TmdbShow tmdbShow, IReadOnlyList<EpisodeInfo> episodes, string? topLevelShokoGroupId, AnidbAnimeInfo[] anidbAnime, ShokoSeriesInfo[] shokoSeries) {
        var tags = new List<string>();
        var genres = new List<string>();
        if (Plugin.Instance.Configuration.TagSources.HasFlag(TagFilter.TagSource.TmdbKeywords))
            tags.AddRange(tmdbShow.Keywords);
        if (Plugin.Instance.Configuration.TagSources.HasFlag(TagFilter.TagSource.TmdbGenres))
            tags.AddRange(tmdbShow.Genres);
        if (Plugin.Instance.Configuration.GenreSources.HasFlag(TagFilter.TagSource.TmdbKeywords))
            genres.AddRange(tmdbShow.Keywords);
        if (Plugin.Instance.Configuration.GenreSources.HasFlag(TagFilter.TagSource.TmdbGenres))
            genres.AddRange(tmdbShow.Genres);
        AddYearlySeasons(ref genres, ref tags, tmdbSeason.YearlySeasons);

        _client = client;
        Id = IdPrefix.TmdbShow + tmdbSeason.Id;
        ExtraIds = [];
        TopLevelShokoGroupId = topLevelShokoGroupId;
        StructureType = SeriesStructureType.TMDB_SeriesAndMovies;
        SeasonOrdering = Ordering.OrderType.None;
        SpecialsPlacement = Ordering.SpecialOrderType.Excluded;
        Type = SeriesType.TV;
        IsMultiEntry = true;
        IsRestricted = tmdbShow.IsRestricted;
        Title = tmdbSeason.Title;
        Titles = tmdbSeason.Titles;
        Overview = tmdbSeason.Overview;
        Overviews = tmdbSeason.Overviews;
        OriginalLanguageCode = tmdbShow.OriginalLanguage;
        CommunityRating = tmdbShow.UserRating;
        if (episodes.Count > 0) {
            PremiereDate = episodes[0].AiredAt;
            EndDate = (tmdbShow.LastAiredAt.HasValue || tmdbSeason.SeasonNumber < tmdbShow.SeasonCount) && episodes[^1].AiredAt is { } endDate && endDate < DateTime.Now ? endDate : null;
        }
        CreatedAt = tmdbSeason.CreatedAt;
        LastUpdatedAt = tmdbSeason.LastUpdatedAt;
        Genres = genres.Distinct().Order().ToArray();
        Tags = tags.Distinct().Order().ToArray();
        Studios = tmdbShow.Studios.Select(s => s.Name).Distinct().Order().ToArray();
        ProductionLocations = new Dictionary<ProviderName, IReadOnlyList<string>>() {
            { ProviderName.TMDB, tmdbShow.ProductionCountries.Values.ToArray() },
        };
        ContentRatings = tmdbShow.ContentRatings;
        DaysOfWeek = episodes
            .Select(e => e.AiredAt)
            .WhereNotNullOrDefault()
            .Distinct()
            .Order()
            // A single cour season is usually 12-13 episodes, give or take.
            .TakeLast(12)
            // In case the two first episodes got an early screening in a 12-13 episode single cour anime.
            .Skip(2)
            .Select(e => e.DayOfWeek)
            .Distinct()
            .Order()
            .ToArray();
        YearlySeasons = tmdbSeason.YearlySeasons;
        Staff = episodes.SelectMany(s => s.Staff).DistinctBy(p => new { p.Type, p.Name, p.Role }).ToArray();
        EpisodeList = tmdbSeason.SeasonNumber is not 0 ? episodes.ToList() : [];
        AlternateEpisodesList = [];
        ExtrasList = [];
        SpecialsList = tmdbSeason.SeasonNumber is 0 ? episodes.ToList() : [];
        SpecialsBeforeEpisodes = new HashSet<string>();
        SpecialsAnchors = new Dictionary<string, EpisodeInfo>();
        Relations = [];
        RelationMap = new Dictionary<string, RelationType>();
        ShokoSeries = shokoSeries ?? [];
        AnidbAnime = anidbAnime ?? [];
        TmdbSeasons = [tmdbSeason.ToInfo()];
        TmdbMovies = [];
    }

    public SeasonInfo(ShokoApiClient client, TmdbMovie tmdbMovie, EpisodeInfo episodeInfo, string? topLevelShokoGroupId, AnidbAnimeInfo[] anidbAnime, ShokoSeriesInfo[] shokoSeries) {
        var genres = episodeInfo.Genres.ToList();
        var tags = episodeInfo.Tags.ToList();
        AddYearlySeasons(ref genres, ref tags, tmdbMovie.YearlySeasons);

        _client = client;
        Id = IdPrefix.TmdbMovie + tmdbMovie.Id.ToString();
        ExtraIds = [];
        TopLevelShokoGroupId = topLevelShokoGroupId;
        StructureType = SeriesStructureType.TMDB_SeriesAndMovies;
        SeasonOrdering = Ordering.OrderType.None;
        SpecialsPlacement = Ordering.SpecialOrderType.Excluded;
        Type = SeriesType.Movie;
        IsMultiEntry = false;
        IsRestricted = tmdbMovie.IsRestricted;
        Title = tmdbMovie.Title;
        Titles = tmdbMovie.Titles;
        Overview = tmdbMovie.Overview;
        Overviews = tmdbMovie.Overviews;
        OriginalLanguageCode = tmdbMovie.OriginalLanguage;
        CommunityRating = episodeInfo.CommunityRating;
        PremiereDate = episodeInfo.AiredAt;
        CreatedAt = tmdbMovie.CreatedAt;
        LastUpdatedAt = tmdbMovie.LastUpdatedAt;
        EndDate = episodeInfo.AiredAt is { } endDate && endDate < DateTime.Now ? endDate : null;
        Genres = genres.Distinct().Order().ToArray();
        Tags = tags.Distinct().Order().ToArray();
        Studios = episodeInfo.Studios;
        ProductionLocations = episodeInfo.ProductionLocations;
        ContentRatings = episodeInfo.ContentRatings;
        DaysOfWeek = [];
        YearlySeasons = tmdbMovie.YearlySeasons;
        Staff = episodeInfo.Staff;
        EpisodeList = [episodeInfo];
        AlternateEpisodesList = [];
        ExtrasList = [];
        SpecialsList = [];
        SpecialsBeforeEpisodes = new HashSet<string>();
        SpecialsAnchors = new Dictionary<string, EpisodeInfo>();
        Relations = [];
        RelationMap = new Dictionary<string, RelationType>();
        ShokoSeries = shokoSeries;
        AnidbAnime = anidbAnime;
        TmdbSeasons = [];
        TmdbMovies = [new() {
            TmdbMovieId = tmdbMovie.Id.ToString(),
            TmdbMovieCollectionId = tmdbMovie.CollectionId?.ToString(),
        }];
    }

    public SeasonInfo(ShokoApiClient client, TmdbMovieCollection tmdbMovieCollection, IReadOnlyList<TmdbMovie> movies, IReadOnlyList<EpisodeInfo> episodes, string? topLevelShokoGroupId, AnidbAnimeInfo[] anidbAnime, ShokoSeriesInfo[] shokoSeries) {
        var genres = episodes.SelectMany(m => m.Genres).ToList();
        var tags = episodes.SelectMany(m => m.Genres).ToList();
        AddYearlySeasons(ref genres, ref tags, movies.SelectMany(m => m.YearlySeasons));

        _client = client;
        Id = IdPrefix.TmdbMovieCollection + tmdbMovieCollection.Id.ToString();
        ExtraIds = [];
        TopLevelShokoGroupId = topLevelShokoGroupId;
        StructureType = SeriesStructureType.TMDB_SeriesAndMovies;
        SeasonOrdering = Ordering.OrderType.None;
        SpecialsPlacement = Ordering.SpecialOrderType.Excluded;
        Type = SeriesType.Movie;
        IsMultiEntry = true;
        IsRestricted = movies.Any(movie => movie.IsRestricted);
        Title = tmdbMovieCollection.Title;
        Titles = tmdbMovieCollection.Titles;
        Overview = tmdbMovieCollection.Overview;
        Overviews = tmdbMovieCollection.Overviews;
        OriginalLanguageCode = movies[0].OriginalLanguage;
        CommunityRating = episodes[0].CommunityRating;
        PremiereDate = episodes[0].AiredAt;
        CreatedAt = tmdbMovieCollection.CreatedAt;
        LastUpdatedAt = tmdbMovieCollection.LastUpdatedAt;
        EndDate = episodes[^1].AiredAt is { } endDate && endDate < DateTime.Now ? endDate : null;
        Genres = genres.Distinct().Order().ToArray();
        Tags = tags.Distinct().Order().ToArray();
        ProductionLocations = episodes
            .SelectMany(sI => sI.ProductionLocations)
            .GroupBy(kP => kP.Key, kP => kP.Value)
            .ToDictionary(gB => gB.Key, gB => gB.SelectMany(l => l).Distinct().Order().ToList() as IReadOnlyList<string>);
        ContentRatings = episodes
            .SelectMany(sI => sI.ContentRatings)
            .Distinct()
            .ToList();
        DaysOfWeek = [];
        YearlySeasons = movies.SelectMany(m => m.YearlySeasons).Distinct().Order().ToArray();
        Studios = episodes.SelectMany(m => m.Studios).Distinct().Order().ToArray();
        Staff = episodes.SelectMany(s => s.Staff).DistinctBy(p => new { p.Type, p.Name, p.Role }).ToArray();
        EpisodeList = [.. episodes];
        AlternateEpisodesList = [];
        ExtrasList = [];
        SpecialsList = [];
        SpecialsBeforeEpisodes = new HashSet<string>();
        SpecialsAnchors = new Dictionary<string, EpisodeInfo>();
        Relations = [];
        RelationMap = new Dictionary<string, RelationType>();
        ShokoSeries = shokoSeries;
        AnidbAnime = anidbAnime;
        TmdbSeasons = [];
        TmdbMovies = [
            ..movies.Select(movie => new TmdbMovieInfo {
                TmdbMovieId = movie.Id.ToString(),
                TmdbMovieCollectionId = movie.CollectionId?.ToString(),
            }),
        ];
    }

    private void AddYearlySeasons(ref List<string> genres, ref List<string> tags, IEnumerable<YearlySeason> yearlySeasons) {
        var seasons = yearlySeasons.Select(season => $"{season.Season} {(season.Season is YearlySeasonName.Winter ? $"{season.Year - 1}/{season.Year.ToString().Substring(2, 2)}" : season.Year)}").ToList();
        if (Plugin.Instance.Configuration.TagSources.HasFlag(TagFilter.TagSource.AllYearlySeasons)) {
            tags.AddRange(seasons);
        }
        else if (Plugin.Instance.Configuration.TagSources.HasFlag(TagFilter.TagSource.AllYearlySeasons) && seasons.Count > 0) {
            tags.Add(seasons.First());
        }
        if (Plugin.Instance.Configuration.GenreSources.HasFlag(TagFilter.TagSource.AllYearlySeasons)) {
            genres.AddRange(seasons);
        }
        else if (Plugin.Instance.Configuration.GenreSources.HasFlag(TagFilter.TagSource.AllYearlySeasons) && seasons.Count > 0) {
            genres.Add(seasons.First());
        }
    }

    public async Task<IReadOnlyList<(File file, string seriesId, HashSet<string> episodeIds)>> GetFiles() {
        var list = new List<(File file, string seriesId, HashSet<string> episodeIds)>();
        if (StructureType is SeriesStructureType.TMDB_SeriesAndMovies) {
            if (Id[0] is IdPrefix.TmdbShow) {
                var episodes = (await _client.GetTmdbEpisodesInTmdbSeason(Id[1..]))
                    .Select(e => e.Id)
                    .ToHashSet();
                var files = await _client.GetFilesForTmdbSeason(Id[1..]);
                foreach (var file in files) {
                    if (file.CrossReferences.Where(x => x.Series.Shoko.HasValue && x.Episodes.Any(e => e.Shoko.HasValue && episodes.Overlaps(e.TMDB.Episode))).ToList() is not { Count: > 0 } xrefList)
                        continue;

                    foreach (var xref in xrefList) {
                        var episodeIds = xref.Episodes
                            .Where(e => e.Shoko.HasValue && episodes.Overlaps(e.TMDB.Episode))
                            .SelectMany(e => episodes.Intersect(e.TMDB.Episode))
                            .Select(e => IdPrefix.TmdbShow + e.ToString())
                            .ToHashSet();
                        list.Add((file, xref.Series.Shoko!.Value.ToString(), episodeIds));
                    }

                }
            }
            else if (Id[0] is IdPrefix.TmdbMovie) {
                var files = await _client.GetFilesForTmdbMovie(Id[1..]);
                var movieId = int.Parse(Id[1..]);
                foreach (var file in files) {
                    if (file.CrossReferences.FirstOrDefault(x => x.Series.Shoko.HasValue && x.Episodes.Any(e => e.Shoko.HasValue && e.TMDB.Movie.Contains(movieId))) is not { } xref)
                        continue;

                    list.Add((file, xref.Series.Shoko!.Value.ToString(), [IdPrefix.TmdbMovie + movieId.ToString()]));
                }
            }
            else if (Id[0] is IdPrefix.TmdbMovieCollection) {
                var movies = (await _client.GetTmdbMoviesInMovieCollection(Id[1..]))
                    .Select(m => m.Id)
                    .ToHashSet();
                foreach (var episodeInfo in EpisodeList) {
                    var episodeFiles = await _client.GetFilesForTmdbMovie(episodeInfo.Id[1..]);
                    var movieId = int.Parse(episodeInfo.Id[1..]);
                    foreach (var file in episodeFiles) {
                        if (file.CrossReferences.FirstOrDefault(x => x.Series.Shoko.HasValue && x.Episodes.Any(e => e.Shoko.HasValue && e.TMDB.Movie.Contains(movieId))) is not { } xref)
                            continue;

                        list.Add((file, xref.Series.Shoko!.Value.ToString(), [IdPrefix.TmdbMovie + movieId.ToString()]));
                    }
                }
            }
        }
        else {
            list.AddRange(
                (await _client.GetFilesForShokoSeries(Id))
                    .Select(file => (
                        file,
                        Id,
                        file.CrossReferences.FirstOrDefault(x => x.Series.Shoko.HasValue && x.Series.Shoko!.Value.ToString() == Id)?.Episodes.Select(e => e.Shoko!.Value.ToString()).ToHashSet() ?? []
                    ))
            );
            foreach (var extraId in ExtraIds)
                list.AddRange(
                    (await _client.GetFilesForShokoSeries(extraId))
                        .Select(file => (
                            file,
                            extraId,
                            file.CrossReferences.FirstOrDefault(x => x.Series.Shoko.HasValue && x.Series.Shoko!.Value.ToString() == extraId)?.Episodes.Select(e => e.Shoko!.Value.ToString()).ToHashSet() ?? []
                        ))
                );
        }

        return list;
    }

    public async Task<Images> GetImages(CancellationToken cancellationToken)
        => Id[0] switch {
                IdPrefix.TmdbShow => await _client.GetImagesForTmdbSeason(Id[1..], cancellationToken),
                IdPrefix.TmdbMovie => await _client.GetImagesForTmdbMovie(Id[1..], cancellationToken),
                IdPrefix.TmdbMovieCollection => await _client.GetImagesForTmdbMovieCollection(Id[1..], cancellationToken),
                _ => await _client.GetImagesForShokoSeries(Id, cancellationToken),
            } ?? new();

    public bool IsExtraEpisode(EpisodeInfo? episodeInfo)
        => episodeInfo != null && ExtrasList.Any(eI => eI.Id == episodeInfo.Id);

    public bool IsEmpty(int offset = 0) {
        // The extra "season" for this season info.
        if (offset == 1)
            return EpisodeList.Count == 0 || !AlternateEpisodesList.Any(eI => eI.IsAvailable);

        // The default "season" for this season info.
        var episodeList = EpisodeList.Count == 0 ? AlternateEpisodesList : EpisodeList;
        if (!episodeList.Any(eI => eI.IsAvailable))
            return false;

        return true;
    }
}
