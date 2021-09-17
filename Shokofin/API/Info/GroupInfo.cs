
using System.Collections.Generic;
using Shokofin.API.Models;

namespace Shokofin.API.Info
{
    public class GroupInfo
    {
        public string Id;

        public Group Shoko;

        public string[] Tags;

        public string[] Genres;

        public string[] Studios;

        public SeriesInfo GetSeriesInfoBySeasonNumber(int seasonNumber) {
            if (seasonNumber == 0 || !(SeasonOrderDictionary.TryGetValue(seasonNumber, out var seriesInfo) && seriesInfo != null))
                return null;

            return seriesInfo;
        }

        public List<SeriesInfo> SeriesList;

        public Dictionary<int, SeriesInfo> SeasonOrderDictionary;

        public Dictionary<SeriesInfo, int> SeasonNumberBaseDictionary;

        public SeriesInfo DefaultSeries;

        public int DefaultSeriesIndex;
    }
}
