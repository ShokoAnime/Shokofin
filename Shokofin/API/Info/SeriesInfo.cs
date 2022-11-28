using System.Collections.Generic;
using System.Linq;
using Shokofin.API.Models;

using PersonInfo = MediaBrowser.Controller.Entities.PersonInfo;
using PersonType = MediaBrowser.Model.Entities.PersonType;

#nullable enable
namespace Shokofin.API.Info;

public class SeriesInfo
{
    public string Id;

    public Series Shoko;

    public Series.AniDBWithDate AniDB;

    public Series.TvDB? TvDB;

    public string[] Tags;

    public string[] Genres;

    public string[] Studios;

    public PersonInfo[] Staff;

    /// <summary>
    /// All episodes (of all type) that belong to this series.
    /// 
    /// Unordered.
    /// </summary>
    public List<EpisodeInfo> RawEpisodeList;

    /// <summary>
    /// A pre-filtered list of normal episodes that belong to this series.
    /// 
    /// Ordered by AniDb air-date.
    /// </summary>
    public List<EpisodeInfo> EpisodeList;

    /// <summary>
    /// A pre-filtered list of "unknown" episodes that belong to this series.
    /// 
    /// Ordered by AniDb air-date.
    /// </summary>
    public List<EpisodeInfo> AlternateEpisodesList;

    /// <summary>
    /// A pre-filtered list of "other" episodes that belong to this series.
    /// 
    /// Ordered by AniDb air-date.
    /// </summary>
    public List<EpisodeInfo> OthersList;

    /// <summary>
    /// A pre-filtered list of "extra" videos that belong to this series.
    /// 
    /// Ordered by AniDb air-date.
    /// </summary>
    public List<EpisodeInfo> ExtrasList;

    /// <summary>
    /// A dictionary holding mappings for the previous normal episode for every special episode in a series.
    /// </summary>
    public Dictionary<EpisodeInfo, EpisodeInfo> SpecialsAnchors;

    /// <summary>
    /// A pre-filtered list of special episodes without an ExtraType
    /// attached.
    ///
    /// Ordered by AniDb episode number.
    /// </summary>
    public List<EpisodeInfo> SpecialsList;

    public SeriesInfo(Series series, List<EpisodeInfo> episodes, IEnumerable<Role> cast, string[] genres, string[] tags)
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
        var specialsAnchorDictionary = new Dictionary<EpisodeInfo, EpisodeInfo>();
        var specialsList = new List<EpisodeInfo>();
        var episodesList = new List<EpisodeInfo>();
        var extrasList = new List<EpisodeInfo>();
        var altEpisodesList = new List<EpisodeInfo>();
        var othersList = new List<EpisodeInfo>();

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
                    othersList.Add(episode);
                    break;
                case EpisodeType.Unknown:
                    altEpisodesList.Add(episode);
                    break;
                default:
                    if (episode.ExtraType != null)
                        extrasList.Add(episode);
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

        // While the filtered specials list is ordered by episode number
        specialsList = specialsList
            .OrderBy(e => e.AniDB.EpisodeNumber)
            .ToList();

        Id = seriesId;
        Shoko = series;
        AniDB = series.AniDBEntity;
        TvDB = series.TvDBEntityList.FirstOrDefault();
        Tags = tags;
        Genres = genres;
        Studios = studios;
        Staff = staff;
        RawEpisodeList = episodes;
        EpisodeList = episodesList;
        AlternateEpisodesList = altEpisodesList;
        OthersList = othersList;
        ExtrasList = extrasList;
        SpecialsAnchors = specialsAnchorDictionary;
        SpecialsList = specialsList;
    }

    private string? GetImagePath(Image image)
    {
        return image != null && image.IsAvailable ? image.ToURLString() : null;
    }

    private PersonInfo? RoleToPersonInfo(Role role)
    {
        switch (role.Type) {
                default:
                    return null;
                case CreatorRoleType.Director:
                    return new PersonInfo {
                        Type = PersonType.Director,
                        Name = role.Staff.Name,
                        Role = role.Name,
                        ImageUrl = GetImagePath(role.Staff.Image),
                    };
                case CreatorRoleType.Producer:
                    return new PersonInfo {
                        Type = PersonType.Producer,
                        Name = role.Staff.Name,
                        Role = role.Name,
                        ImageUrl = GetImagePath(role.Staff.Image),
                    };
                case CreatorRoleType.Music:
                    return new PersonInfo {
                        Type = PersonType.Lyricist,
                        Name = role.Staff.Name,
                        Role = role.Name,
                        ImageUrl = GetImagePath(role.Staff.Image),
                    };
                case CreatorRoleType.SourceWork:
                    return new PersonInfo {
                        Type = PersonType.Writer,
                        Name = role.Staff.Name,
                        Role = role.Name,
                        ImageUrl = GetImagePath(role.Staff.Image),
                    };
                case CreatorRoleType.SeriesComposer:
                    return new PersonInfo {
                        Type = PersonType.Composer,
                        Name = role.Staff.Name,
                        ImageUrl = GetImagePath(role.Staff.Image),
                    };
                case CreatorRoleType.Seiyuu:
                    return new PersonInfo {
                        Type = PersonType.Actor,
                        Name = role.Staff.Name,
                        // The character will always be present if the role is a VA.
                        // We make it a conditional check since otherwise will the compiler complain.
                        Role = role.Character?.Name ?? "",
                        ImageUrl = GetImagePath(role.Staff.Image),
                    };
            }
    }
}
