
using System.Collections.Generic;
using Shokofin.API.Models;

namespace Shokofin.API.Info
{
    public class GroupInfo
    {
        public string Id;

        /// <summary>
        /// Shared Guid for series merging.
        /// </summary>
        public System.Guid Guid;

        public Group Shoko;

        public List<SeriesInfo> SeriesList;

        public SeriesInfo DefaultSeries;

        public int DefaultSeriesIndex;
    }
}
