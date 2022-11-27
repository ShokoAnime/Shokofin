
using System.Collections.Generic;
using System.Linq;
using Shokofin.API.Models;
using Shokofin.Utils;

#nullable enable
namespace Shokofin.API.Info;

public class GroupInfo
{
    public string Id;

    public Group Shoko;

    public string[] Tags;

    public string[] Genres;

    public string[] Studios;

    public List<SeriesInfo> SeriesList;

    public Dictionary<int, SeriesInfo> SeasonOrderDictionary;

    public Dictionary<SeriesInfo, int> SeasonNumberBaseDictionary;

    public SeriesInfo? DefaultSeries;

    public GroupInfo(Group group)
    {
        Id = group.IDs.Shoko.ToString();
        Shoko = group;
        Tags = new string[0];
        Genres = new string[0];
        Studios = new string[0];
        SeriesList = new();
        SeasonNumberBaseDictionary = new();
        SeasonOrderDictionary = new();
        DefaultSeries = null;
    }

    public GroupInfo(Group group, List<SeriesInfo> seriesList, Ordering.GroupFilterType filterByType)
    {
        var groupId = group.IDs.Shoko.ToString();

        // Order series list
        var orderingType = filterByType == Ordering.GroupFilterType.Movies ? Plugin.Instance.Configuration.MovieOrdering : Plugin.Instance.Configuration.SeasonOrdering;
        switch (orderingType) {
            case Ordering.OrderType.Default:
                break;
            case Ordering.OrderType.ReleaseDate:
                seriesList = seriesList.OrderBy(s => s?.AniDB?.AirDate ?? System.DateTime.MaxValue).ToList();
                break;
            // Should not be selectable unless a user fiddles with DevTools in the browser to select the option.
            case Ordering.OrderType.Chronological:
                throw new System.Exception("Not implemented yet");
        }

        // Select the targeted id if a group spesify a default series.
        int foundIndex = -1;
        int targetId = group.IDs.MainSeries;
        if (targetId != 0)
            foundIndex = seriesList.FindIndex(s => s.Shoko.IDs.Shoko == targetId);
        // Else select the default series as first-to-be-released.
        else switch (orderingType) {
            // The list is already sorted by release date, so just return the first index.
            case Ordering.OrderType.ReleaseDate:
                foundIndex = 0;
                break;
            // We don't know how Shoko may have sorted it, so just find the earliest series
            case Ordering.OrderType.Default:
            // We can't be sure that the the series in the list was _released_ chronologically, so find the earliest series, and use that as a base.
            case Ordering.OrderType.Chronological: {
                var earliestSeries = seriesList.Aggregate((cur, nxt) => (cur == null || (nxt.AniDB.AirDate ?? System.DateTime.MaxValue) < (cur.AniDB.AirDate ?? System.DateTime.MaxValue)) ? nxt : cur);
                foundIndex = seriesList.FindIndex(s => s == earliestSeries);
                break;
            }
        }

        // Throw if we can't get a base point for seasons.
        if (foundIndex == -1)
            throw new System.Exception("Unable to get a base-point for seasions withing the group");

        var seasonOrderDictionary = new Dictionary<int, SeriesInfo>();
        var seasonNumberBaseDictionary = new Dictionary<SeriesInfo, int>();
        var positiveSeasonNumber = 1;
        var negativeSeasonNumber = -1;
        foreach (var (seriesInfo, index) in seriesList.Select((s, i) => (s, i))) {
            int seasonNumber;
            var offset = 0;
            if (seriesInfo.AlternateEpisodesList.Count > 0)
                offset++;
            if (seriesInfo.OthersList.Count > 0)
                offset++;

            // Series before the default series get a negative season number
            if (index < foundIndex) {
                seasonNumber = negativeSeasonNumber;
                negativeSeasonNumber -= offset + 1;
            }
            else {
                seasonNumber = positiveSeasonNumber;
                positiveSeasonNumber += offset + 1;
            }

            seasonNumberBaseDictionary.Add(seriesInfo, seasonNumber);
            seasonOrderDictionary.Add(seasonNumber, seriesInfo);
            for (var i = 0; i < offset; i++)
                seasonOrderDictionary.Add(seasonNumber + (index < foundIndex ? -(i + 1) :  (i + 1)), seriesInfo);
        }

        Id = groupId;
        Shoko = group;
        Tags = seriesList.SelectMany(s => s.Tags).Distinct().ToArray();
        Genres = seriesList.SelectMany(s => s.Genres).Distinct().ToArray();
        Studios = seriesList.SelectMany(s => s.Studios).Distinct().ToArray();
        SeriesList = seriesList;
        SeasonNumberBaseDictionary = seasonNumberBaseDictionary;
        SeasonOrderDictionary = seasonOrderDictionary;
        DefaultSeries = seriesList[foundIndex];
    }

    public SeriesInfo? GetSeriesInfoBySeasonNumber(int seasonNumber) {
        if (seasonNumber == 0 || !(SeasonOrderDictionary.TryGetValue(seasonNumber, out var seriesInfo) && seriesInfo != null))
            return null;

        return seriesInfo;
    }
}
