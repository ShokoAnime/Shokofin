
using System.Collections.Generic;
using Shokofin.API.Models;

namespace Shokofin.API.Info
{
    public class GroupInfo
    {
        public string Id;

        public Group Shoko;

        public SeriesInfo GetSeriesInfoBySeasonNumber(int seasonNumber) {
            if (seasonNumber == 0)
                return null;
            
            int seriesIndex = seasonNumber > 0 ? seasonNumber - 1 : seasonNumber;
            var index = DefaultSeriesIndex + seriesIndex;
            var seriesInfo = SeriesList[index];
            if (seriesInfo == null)
                return null;

            return seriesInfo;
        }

        public List<SeriesInfo> SeriesList;

        public SeriesInfo DefaultSeries;

        public int DefaultSeriesIndex;
    }
}
