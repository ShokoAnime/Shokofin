
using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;
using Shokofin.API.Models;
using Shokofin.Utils;

namespace Shokofin.API.Info;

public class ShowInfo
{
    /// <summary>
    /// Main Shoko Series Id.
    /// </summary>
    public readonly string Id;

    /// <summary>
    /// Main Shoko Group Id.
    /// </summary>
    public readonly string? GroupId;

    /// <summary>
    /// Shoko Group Id used for Collection Support.
    /// </summary>
    public readonly string? CollectionId;

    /// <summary>
    /// The main name of the show.
    /// </summary>
    public readonly string Name;

    /// <summary>
    /// Indicates this is a standalone show without a group attached to it.
    /// </summary>
    public bool IsStandalone =>
        Shoko == null;

    /// <summary>
    /// The Shoko Group, if this is not a standalone show entry.
    /// </summary>
    public readonly Group? Shoko;

    /// <summary>
    /// First premiere date of the show.
    /// </summary>
    public DateTime? PremiereDate =>
        SeasonList
            .Select(s => s.AniDB.AirDate)
            .Where(s => s != null)
            .OrderBy(s => s)
            .FirstOrDefault();

    /// <summary>
    /// Ended date of the show.
    /// </summary>
    public DateTime? EndDate => 
        SeasonList.Any(s => s.AniDB.EndDate == null) ? null : SeasonList
            .Select(s => s.AniDB.AirDate)
            .OrderBy(s => s)
            .LastOrDefault();

    /// <summary>
    /// Overall content rating of the show.
    /// </summary>
    public string? OfficialRating =>
        DefaultSeason.AniDB.Restricted ? "XXX" : null;

    /// <summary>
    /// Custom rating of the show.
    /// </summary>
    public string? CustomRating =>
        DefaultSeason.AniDB.Restricted ? "XXX" : null;

    /// <summary>
    /// Overall community rating of the show.
    /// </summary>
    public float CommunityRating =>
        (float)(SeasonList.Aggregate(0f, (total, seasonInfo) => total + seasonInfo.AniDB.Rating.ToFloat(10)) / SeasonList.Count);

    /// <summary>
    /// All tags from across all seasons.
    /// </summary>
    public readonly IReadOnlyList<string> Tags;

    /// <summary>
    /// All genres from across all seasons.
    /// </summary>
    public readonly IReadOnlyList<string> Genres;

    /// <summary>
    /// All studios from across all seasons.
    /// </summary>
    public readonly IReadOnlyList<string> Studios;

    /// <summary>
    /// All staff from across all seasons.
    /// </summary>
    public readonly IReadOnlyList<PersonInfo> Staff;

    /// <summary>
    /// All seasons.
    /// </summary>
    public readonly List<SeasonInfo> SeasonList;

    /// <summary>
    /// The season order dictionary.
    /// </summary>
    public readonly IReadOnlyDictionary<int, SeasonInfo> SeasonOrderDictionary;

    /// <summary>
    /// The season number base-number dictionary.
    /// </summary>
    private readonly IReadOnlyDictionary<string, int> SeasonNumberBaseDictionary;

    /// <summary>
    /// A pre-filtered set of special episode ids without an ExtraType
    /// attached.
    /// </summary>
    private readonly IReadOnlySet<string> SpecialsSet;

    /// <summary>
    /// Indicates that the show has specials.
    /// </summary>
    public bool HasSpecials =>
        SpecialsSet.Count > 0;

    /// <summary>
    /// The default season for the show.
    /// </summary>
    public readonly SeasonInfo DefaultSeason;

    public ShowInfo(SeasonInfo seasonInfo, string? collectionId = null)
    {
        var seasonNumberBaseDictionary = new Dictionary<string, int>();
        var seasonOrderDictionary = new Dictionary<int, SeasonInfo>();
        var seasonNumberOffset = 0;
        if (seasonInfo.EpisodeList.Count > 0 || seasonInfo.AlternateEpisodesList.Count > 0)
            seasonNumberBaseDictionary.Add(seasonInfo.Id, seasonNumberOffset);
        if (seasonInfo.EpisodeList.Count > 0)
            seasonOrderDictionary.Add(++seasonNumberOffset, seasonInfo);
        if (seasonInfo.AlternateEpisodesList.Count > 0)
            seasonOrderDictionary.Add(++seasonNumberOffset, seasonInfo);
        Id = seasonInfo.Id;
        GroupId = seasonInfo.Shoko.IDs.ParentGroup.ToString();
        CollectionId = collectionId ?? seasonInfo.Shoko.IDs.ParentGroup.ToString();
        Name = seasonInfo.Shoko.Name;
        Tags = seasonInfo.Tags;
        Genres = seasonInfo.Genres;
        Studios = seasonInfo.Studios;
        Staff = seasonInfo.Staff;
        SeasonList = new List<SeasonInfo>() { seasonInfo };
        SeasonNumberBaseDictionary = seasonNumberBaseDictionary;
        SeasonOrderDictionary = seasonOrderDictionary;
        SpecialsSet = seasonInfo.SpecialsList.Select(episodeInfo => episodeInfo.Id).ToHashSet();
        DefaultSeason = seasonInfo;
    }

    public ShowInfo(Group group, List<SeasonInfo> seasonList, ILogger logger, bool useGroupIdForCollection)
    {
        var groupId = group.IDs.Shoko.ToString();

        // Order series list
        var orderingType = Plugin.Instance.Configuration.SeasonOrdering;
        switch (orderingType) {
            case Ordering.OrderType.Default:
                break;
            case Ordering.OrderType.ReleaseDate:
                seasonList = seasonList.OrderBy(s => s?.AniDB?.AirDate ?? DateTime.MaxValue).ToList();
                break;
            case Ordering.OrderType.Chronological:
                seasonList.Sort(new SeriesInfoRelationComparer());
                break;
        }

        // Select the targeted id if a group specify a default series.
        int foundIndex = -1;
        switch (orderingType) {
            case Ordering.OrderType.ReleaseDate:
                foundIndex = 0;
                break;
            case Ordering.OrderType.Default:
            case Ordering.OrderType.Chronological: {
                int targetId = group.IDs.MainSeries;
                foundIndex = seasonList.FindIndex(s => s.Shoko.IDs.Shoko == targetId);
                break;
            }
        }

        // Fallback to the first series if we can't get a base point for seasons.
        if (foundIndex == -1) {
            logger.LogWarning("Unable to get a base-point for seasons within the group for the filter, so falling back to the first series in the group. This is most likely due to library separation being enabled. (Group={GroupID})", groupId);
            foundIndex = 0;
        }

        var defaultSeason = seasonList[foundIndex];
        var specialsSet = new HashSet<string>();
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
                specialsSet.Add(episodeInfo.Id);
        }

        Id = defaultSeason.Id;
        GroupId = groupId;
        Name = group.Name;
        Shoko = group;
        CollectionId = useGroupIdForCollection ? groupId : group.IDs.ParentGroup?.ToString();
        Tags = seasonList.SelectMany(s => s.Tags).Distinct().ToArray();
        Genres = seasonList.SelectMany(s => s.Genres).Distinct().ToArray();
        Studios = seasonList.SelectMany(s => s.Studios).Distinct().ToArray();
        Staff = seasonList.SelectMany(s => s.Staff).DistinctBy(p => new { p.Type, p.Name, p.Role }).ToArray();
        SeasonList = seasonList;
        SeasonNumberBaseDictionary = seasonNumberBaseDictionary;
        SeasonOrderDictionary = seasonOrderDictionary;
        SpecialsSet = specialsSet;
        DefaultSeason = defaultSeason;
    }

    public bool IsSpecial(EpisodeInfo episodeInfo)
        => SpecialsSet.Contains(episodeInfo.Id);

    public bool TryGetBaseSeasonNumberForSeasonInfo(SeasonInfo season, out int baseSeasonNumber)
        => SeasonNumberBaseDictionary.TryGetValue(season.Id, out baseSeasonNumber);

    public int GetBaseSeasonNumberForSeasonInfo(SeasonInfo season)
        => SeasonNumberBaseDictionary.TryGetValue(season.Id, out var baseSeasonNumber) ? baseSeasonNumber : 0;

    public SeasonInfo? GetSeasonInfoBySeasonNumber(int seasonNumber)
    {
        if (seasonNumber == 0 || !(SeasonOrderDictionary.TryGetValue(seasonNumber, out var seasonInfo) && seasonInfo != null))
            return null;

        return seasonInfo;
    }
}
