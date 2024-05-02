using System;
using System.Collections.Generic;
using System.Linq;
using Shokofin.API.Models;

using PersonInfo = MediaBrowser.Controller.Entities.PersonInfo;
using PersonType = MediaBrowser.Model.Entities.PersonType;

namespace Shokofin.API.Info;

public class SeasonInfo
{
    public readonly string Id;

    public readonly Series Shoko;

    public readonly Series.AniDBWithDate AniDB;

    public readonly Series.TvDB? TvDB;

    public readonly SeriesType Type;

    /// <summary>
    /// The date of the earliest imported file, or when the series was created
    /// in shoko if no files are imported yet.
    /// </summary>
    public readonly DateTime? EarliestImportedAt;

    /// <summary>
    /// The date of the last imported file, or when the series was created
    /// in shoko if no files are imported yet.
    /// </summary>
    public readonly DateTime? LastImportedAt;

    public readonly IReadOnlyList<string> Tags;

    public readonly IReadOnlyList<string> Genres;

    public readonly IReadOnlyList<string> Studios;

    public readonly IReadOnlyList<PersonInfo> Staff;

    /// <summary>
    /// All episodes (of all type) that belong to this series.
    ///
    /// Unordered.
    /// </summary>
    public readonly IReadOnlyList<EpisodeInfo> RawEpisodeList;

    /// <summary>
    /// A pre-filtered list of normal episodes that belong to this series.
    ///
    /// Ordered by AniDb air-date.
    /// </summary>
    public readonly List<EpisodeInfo> EpisodeList;

    /// <summary>
    /// A pre-filtered list of "unknown" episodes that belong to this series.
    ///
    /// Ordered by AniDb air-date.
    /// </summary>
    public readonly List<EpisodeInfo> AlternateEpisodesList;

    /// <summary>
    /// A pre-filtered list of "extra" videos that belong to this series.
    ///
    /// Ordered by AniDb air-date.
    /// </summary>
    public readonly List<EpisodeInfo> ExtrasList;

    /// <summary>
    /// A dictionary holding mappings for the previous normal episode for every special episode in a series.
    /// </summary>
    public readonly IReadOnlyDictionary<EpisodeInfo, EpisodeInfo> SpecialsAnchors;

    /// <summary>
    /// A pre-filtered list of special episodes without an ExtraType
    /// attached.
    ///
    /// Ordered by AniDb episode number.
    /// </summary>
    public readonly List<EpisodeInfo> SpecialsList;

    /// <summary>
    /// Related series data available in Shoko.
    /// </summary>
    public readonly IReadOnlyList<Relation> Relations;

    /// <summary>
    /// Map of related series with type.
    /// </summary>
    public readonly IReadOnlyDictionary<string, RelationType> RelationMap;

    public SeasonInfo(Series series, DateTime? earliestImportedAt, DateTime? lastImportedAt, List<EpisodeInfo> episodes, List<Role> cast, List<Relation> relations, string[] genres, string[] tags)
    {
        var seriesId = series.IDs.Shoko.ToString();
        var studios = cast
            .Where(r => r.Type == CreatorRoleType.Studio)
            .Select(r => r.Staff.Name)
            .ToArray();
        var staff = cast
            .Select(RoleToPersonInfo)
            .OfType<PersonInfo>()
            .ToArray();
        var relationMap = relations
            .Where(r => r.RelatedIDs.Shoko.HasValue)
            .ToDictionary(r => r.RelatedIDs.Shoko!.Value.ToString(), r => r.Type);
        var specialsAnchorDictionary = new Dictionary<EpisodeInfo, EpisodeInfo>();
        var specialsList = new List<EpisodeInfo>();
        var episodesList = new List<EpisodeInfo>();
        var extrasList = new List<EpisodeInfo>();
        var altEpisodesList = new List<EpisodeInfo>();

        // Iterate over the episodes once and store some values for later use.
        int index = 0;
        int lastNormalEpisode = 0;
        foreach (var episode in episodes) {
            switch (episode.AniDB.Type) {
                case EpisodeType.Normal:
                    episodesList.Add(episode);
                    lastNormalEpisode = index;
                    break;
                case EpisodeType.Other:
                    altEpisodesList.Add(episode);
                    break;
                default:
                    if (episode.ExtraType != null) {
                        extrasList.Add(episode);
                    }
                    else if (episode.AniDB.Type == EpisodeType.Special) {
                        specialsList.Add(episode);
                        var previousEpisode = episodes
                            .GetRange(lastNormalEpisode, index - lastNormalEpisode)
                            .FirstOrDefault(e => e.AniDB.Type == EpisodeType.Normal);
                        if (previousEpisode != null)
                            specialsAnchorDictionary[episode] = previousEpisode;
                    }
                    break;
            }
            index++;
        }

        // We order the lists after sorting them into buckets because the bucket
        // sort we're doing above have the episodes ordered by air date to get
        // the previous episode anchors right.
        episodesList = episodesList
            .OrderBy(e => e.AniDB.EpisodeNumber)
            .ToList();
        specialsList = specialsList
            .OrderBy(e => e.AniDB.EpisodeNumber)
            .ToList();
        altEpisodesList = altEpisodesList
            .OrderBy(e => e.AniDB.EpisodeNumber)
            .ToList();

        // Replace the normal episodes if we've hidden all the normal episodes and we have at least one
        // alternate episode locally.
        var type = series.AniDBEntity.Type;
        if (episodesList.Count == 0 && altEpisodesList.Count > 0) {
            // Switch the type from movie to web if we've hidden the main movie, and we have some of the parts.
            if (type == SeriesType.Movie)
                type = SeriesType.Web;

            episodesList = altEpisodesList;
            altEpisodesList = new();
        }

        if (Plugin.Instance.Configuration.MovieSpecialsAsExtraFeaturettes && type == SeriesType.Movie && specialsList.Count > 0) {
            extrasList.AddRange(specialsList);
            specialsAnchorDictionary.Clear();
            specialsList = new();
        }

        Id = seriesId;
        Shoko = series;
        AniDB = series.AniDBEntity;
        TvDB = series.TvDBEntityList.FirstOrDefault();
        Type = type;
        EarliestImportedAt = earliestImportedAt;
        LastImportedAt = lastImportedAt;
        Tags = tags;
        Genres = genres;
        Studios = studios;
        Staff = staff;
        RawEpisodeList = episodes;
        EpisodeList = episodesList;
        AlternateEpisodesList = altEpisodesList;
        ExtrasList = extrasList;
        SpecialsAnchors = specialsAnchorDictionary;
        SpecialsList = specialsList;
        Relations = relations;
        RelationMap = relationMap;
    }

    public bool IsExtraEpisode(EpisodeInfo episodeInfo)
        => ExtrasList.Any(eI => eI.Id == episodeInfo.Id);

    public bool IsEmpty(int offset = 0)
    {
        // The extra "season" for this season info.
        if (offset == 1)
            return EpisodeList.Count == 0 || !AlternateEpisodesList.Any(eI => eI.Shoko.Size > 0);

        // The default "season" for this season info.
        var episodeList = EpisodeList.Count == 0 ? AlternateEpisodesList : EpisodeList;
        if (!episodeList.Any(eI => eI.Shoko.Size > 0))
            return false;

        return true;
    }

    private static string? GetImagePath(Image image)
        => image != null && image.IsAvailable ? image.ToURLString() : null;

    private static PersonInfo? RoleToPersonInfo(Role role)
        => role.Type switch
        {
            CreatorRoleType.Director => new PersonInfo
            {
                Type = PersonType.Director,
                Name = role.Staff.Name,
                Role = role.Name,
                ImageUrl = GetImagePath(role.Staff.Image),
            },
            CreatorRoleType.Producer => new PersonInfo
            {
                Type = PersonType.Producer,
                Name = role.Staff.Name,
                Role = role.Name,
                ImageUrl = GetImagePath(role.Staff.Image),
            },
            CreatorRoleType.Music => new PersonInfo
            {
                Type = PersonType.Lyricist,
                Name = role.Staff.Name,
                Role = role.Name,
                ImageUrl = GetImagePath(role.Staff.Image),
            },
            CreatorRoleType.SourceWork => new PersonInfo
            {
                Type = PersonType.Writer,
                Name = role.Staff.Name,
                Role = role.Name,
                ImageUrl = GetImagePath(role.Staff.Image),
            },
            CreatorRoleType.SeriesComposer => new PersonInfo
            {
                Type = PersonType.Composer,
                Name = role.Staff.Name,
                ImageUrl = GetImagePath(role.Staff.Image),
            },
            CreatorRoleType.Seiyuu => new PersonInfo
            {
                Type = PersonType.Actor,
                Name = role.Staff.Name,
                // The character will always be present if the role is a VA.
                // We make it a conditional check since otherwise will the compiler complain.
                Role = role.Character?.Name ?? string.Empty,
                ImageUrl = GetImagePath(role.Staff.Image),
            },
            _ => null,
        };
}
