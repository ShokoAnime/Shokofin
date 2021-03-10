
using System.Collections.Generic;
using Shokofin.API.Models;

namespace Shokofin.API.Info
{
    public class GroupInfo
    {
        public string ID;
        public Group Shoko;
        public List<SeriesInfo> SeriesList;
        public SeriesInfo DefaultSeries;
        public int DefaultSeriesIndex;
    }

}
