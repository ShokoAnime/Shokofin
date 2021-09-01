using System.Collections.Generic;
using Shokofin.API.Models;

namespace Shokofin.API.Info
{
    public class SeriesInfo
    {
        public string Id;

        public Series Shoko;

        public Series.AniDB AniDB;

        public string TvDBId;

        public Series.TvDB TvDB;

        /// <summary>
        /// All episodes (of all type) that belong to this series.
        /// 
        /// Ordered by AniDb air-date.
        /// </summary>
        public List<EpisodeInfo> EpisodeList;

        /// <summary>
        /// The number of normal episodes in this series.
        /// </summary>
        public int EpisodeCount;

        /// <summary>
        /// A dictionary holding mappings for the previous normal episode for every special episode in a series.
        /// </summary>
        public Dictionary<string, string> SpesialsAnchors;

        /// <summary>
        /// A pre-filtered list of special episodes without an ExtraType
        /// attached.
        ///
        /// Ordered by AniDb episode number.
        /// </summary>
        public List<EpisodeInfo> SpecialsList;
    }
}
