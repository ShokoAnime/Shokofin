using System.Collections.Generic;
using Shokofin.API.Models;

using PersonInfo = MediaBrowser.Controller.Entities.PersonInfo;

namespace Shokofin.API.Info
{
    public class SeriesInfo
    {
        public string Id;

        public Series Shoko;

        public Series.AniDB AniDB;

        public string TvDBId;

        public Series.TvDB TvDB;

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
        public Dictionary<EpisodeInfo, EpisodeInfo> SpesialsAnchors;

        /// <summary>
        /// A pre-filtered list of special episodes without an ExtraType
        /// attached.
        ///
        /// Ordered by AniDb episode number.
        /// </summary>
        public List<EpisodeInfo> SpecialsList;
    }
}
