using System;
using System.Collections.Generic;

namespace Shokofin.API.Models
{
    public class Series : BaseModel
    {
        public SeriesIDs IDs { get; set; }

        public Images Images { get; set; }

        public Rating UserRating { get; set; }

        public List<Resource> Links { get; set; }

        public DateTime Created { get; set; }

        public DateTime Updated { get; set; }

        public class AniDB
        {
            public int ID { get; set; }

            public SeriesType Type { get; set; }

            public string Title { get; set; }

            public bool Restricted { get; set; }

            public DateTime? AirDate { get; set; }

            public DateTime? EndDate { get; set; }

            public List<Title> Titles { get; set; }

            public string Description { get; set; }

            public Image Poster { get; set; }

            public Rating Rating { get; set; }

        }

        public class TvDB
        {
            public int ID { get; set; }

            public DateTime? AirDate { get; set; }

            public DateTime? EndDate { get; set; }

            public string Title { get; set; }

            public string Description { get; set; }

            public int? Season { get; set; }

            public List<Image> Posters { get; set; }

            public List<Image> Fanarts { get; set; }

            public List<Image> Banners { get; set; }

            public Rating Rating { get; set; }
        }

        public class Resource
        {
            public string name { get; set; }

            public string url { get; set; }

            public Image image { get; set; }
        }
    }

    public class SeriesIDs : IDs
    {
        public int AniDB { get; set; }

        public List<int> TvDB { get; set; } = new List<int>();

        public List<int> MovieDB { get; set; } = new List<int>();

        public List<int> MAL { get; set; } = new List<int>();

        public List<string> TraktTv { get; set; } = new List<string>();

        public List<int> AniList { get; set; } = new List<int>();
    }

    public class SeriesSearchResult : Series
    {
        public string Match { get; set; }

        public double Distance { get; set; }
    }

    public enum SeriesType
    {
        /// <summary>
        /// The series type is unknown.
        /// </summary>
        Unknown = 0,
        /// <summary>
        /// A catch-all type for future extensions when a provider can't use a current episode type, but knows what the future type should be.
        /// </summary>
        Other = 1,
        /// <summary>
        /// Standard TV series.
        /// </summary>
        TV = 2,
        /// <summary>
        /// TV special.
        /// </summary>
        TVSpecial = 3,
        /// <summary>
        /// Web series.
        /// </summary>
        Web = 4,
        /// <summary>
        /// All movies, regardless of source (e.g. web or theater)
        /// </summary>
        Movie = 5,
        /// <summary>
        /// Original Video Animations, AKA standalone releases that don't air on TV or the web.
        /// </summary>
        OVA = 6,
    }
}
