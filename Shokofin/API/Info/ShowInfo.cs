
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Shokofin.API.Models;
using Shokofin.Utils;

#nullable enable
namespace Shokofin.API.Info;

public class ShowInfo
{
    public string? Id;

    public string? ParentId;

    public string Name;

    public bool IsStandalone =>
        Shoko == null;

    public Group? Shoko;

    public string[] Tags;

    public string[] Genres;

    public string[] Studios;

    public List<SeasonInfo> SeasonList;

    public Dictionary<int, SeasonInfo> SeasonOrderDictionary;

    public Dictionary<SeasonInfo, int> SeasonNumberBaseDictionary;

    public SeasonInfo? DefaultSeason;

    public ShowInfo(Series series)
    {
        Id = null;
        ParentId = series.IDs.ParentGroup.ToString();
        Name = series.Name;
        Tags = System.Array.Empty<string>();
        Genres = System.Array.Empty<string>();
        Studios = System.Array.Empty<string>();
        SeasonList = new();
        SeasonNumberBaseDictionary = new();
        SeasonOrderDictionary = new();
        DefaultSeason = null;
    }

    public ShowInfo(Group group)
    {
        Id = group.IDs.Shoko.ToString();
        Name = group.Name;
        Shoko = group;
        ParentId = group.IDs.ParentGroup?.ToString();
        Tags = System.Array.Empty<string>();
        Genres = System.Array.Empty<string>();
        Studios = System.Array.Empty<string>();
        SeasonList = new();
        SeasonNumberBaseDictionary = new();
        SeasonOrderDictionary = new();
        DefaultSeason = null;
    }

    public ShowInfo(SeasonInfo seasonInfo)
    {
        var seasonNumberBaseDictionary = new Dictionary<SeasonInfo, int>() { { seasonInfo, 1 } };
        var seasonOrderDictionary = new Dictionary<int, SeasonInfo>() { { 1, seasonInfo } };
        var seasonNumberOffset = 1;
        if (seasonInfo.AlternateEpisodesList.Count > 0)
            seasonOrderDictionary.Add(++seasonNumberOffset, seasonInfo);
        if (seasonInfo.OthersList.Count > 0)
            seasonOrderDictionary.Add(++seasonNumberOffset, seasonInfo);

        Id = null;
        ParentId = seasonInfo.Shoko.IDs.ParentGroup.ToString();
        Name = seasonInfo.Shoko.Name;
        Tags = seasonInfo.Tags;
        Genres = seasonInfo.Genres;
        Studios = seasonInfo.Studios;
        SeasonList = new() { seasonInfo };
        SeasonNumberBaseDictionary = seasonNumberBaseDictionary;
        SeasonOrderDictionary = seasonOrderDictionary;
        DefaultSeason = seasonInfo;
    }

    public ShowInfo(Group group, List<SeasonInfo> seriesList, Ordering.GroupFilterType filterByType, ILogger logger)
    {
        var groupId = group.IDs.Shoko.ToString();

        if (seriesList.Count > 0) switch (filterByType) {
            case Ordering.GroupFilterType.Movies:
                seriesList = seriesList.Where(s => s.AniDB.Type == SeriesType.Movie).ToList();
                break;
            case Ordering.GroupFilterType.Others:
                seriesList = seriesList.Where(s => s.AniDB.Type != SeriesType.Movie).ToList();
                break;
        }

        // Order series list
        var orderingType = filterByType == Ordering.GroupFilterType.Movies ? Plugin.Instance.Configuration.MovieOrdering : Plugin.Instance.Configuration.SeasonOrdering;
        switch (orderingType) {
            case Ordering.OrderType.Default:
                break;
            case Ordering.OrderType.ReleaseDate:
                seriesList = seriesList.OrderBy(s => s?.AniDB?.AirDate ?? System.DateTime.MaxValue).ToList();
                break;
            case Ordering.OrderType.Chronological:
                seriesList.Sort(new SeriesInfoRelationComparer());
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
                foundIndex = seriesList.FindIndex(s => s.Shoko.IDs.Shoko == targetId);
                break;
            }
        }

        // Fallback to the first series if we can't get a base point for seasons.
        if (foundIndex == -1)
        {
            logger.LogWarning("Unable to get a base-point for seasons within the group for the filter, so falling back to the first series in the group. This is most likely due to library separation being enabled. (Filter={FilterByType},Group={GroupID})", filterByType.ToString(), groupId);
            foundIndex = 0;
        }

        var seasonOrderDictionary = new Dictionary<int, SeasonInfo>();
        var seasonNumberBaseDictionary = new Dictionary<SeasonInfo, int>();
        var seasonNumberOffset = 0;
        foreach (var (seasonInfo, index) in seriesList.Select((s, i) => (s, i))) {
            seasonNumberBaseDictionary.Add(seasonInfo, ++seasonNumberOffset);
            seasonOrderDictionary.Add(seasonNumberOffset, seasonInfo);
            if (seasonInfo.AlternateEpisodesList.Count > 0)
                seasonOrderDictionary.Add(++seasonNumberOffset, seasonInfo);
            if (seasonInfo.OthersList.Count > 0)
                seasonOrderDictionary.Add(++seasonNumberOffset, seasonInfo);
        }

        Id = groupId;
        Name = seriesList.Count > 0 ? seriesList[foundIndex].Shoko.Name : group.Name;
        Shoko = group;
        ParentId = group.IDs.ParentGroup?.ToString();
        Tags = seriesList.SelectMany(s => s.Tags).Distinct().ToArray();
        Genres = seriesList.SelectMany(s => s.Genres).Distinct().ToArray();
        Studios = seriesList.SelectMany(s => s.Studios).Distinct().ToArray();
        SeasonList = seriesList;
        SeasonNumberBaseDictionary = seasonNumberBaseDictionary;
        SeasonOrderDictionary = seasonOrderDictionary;
        DefaultSeason = seriesList.Count > 0 ? seriesList[foundIndex] : null;
    }

    public SeasonInfo? GetSeriesInfoBySeasonNumber(int seasonNumber) {
        if (seasonNumber == 0 || !(SeasonOrderDictionary.TryGetValue(seasonNumber, out var seasonInfo) && seasonInfo != null))
            return null;

        return seasonInfo;
    }
}
