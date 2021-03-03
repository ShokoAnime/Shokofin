using System;
using System.Collections.Generic;

namespace Shokofin.API.Models
{
    public class Episode : BaseModel
    {
        public EpisodeIDs IDs { get; set; }
        
        public DateTime? Watched { get; set; }

        public class AniDB
        {
            public int ID { get; set; }
            
            public string Type { get; set; }
            
            public int EpisodeNumber { get; set; }
            
            public DateTime? AirDate { get; set; }
            
            public List<Title> Titles { get; set; }
            
            public string Description { get; set; }
            
            public Rating Rating { get; set; }
        }

        public class TvDB
        {
            public int ID { get; set; }
            
            public int Season { get; set; }
            
            public int Number { get; set; }
            
            public int AbsoluteNumber { get; set; }
            
            public string Title { get; set; }
            
            public string Description { get; set; }
            
            public DateTime? AirDate { get; set; }
            
            public int AirsAfterSeason { get; set; }
            
            public int AirsBeforeSeason { get; set; }
            
            public int AirsBeforeEpisode { get; set; }
            
            public Rating Rating { get; set; }
            
            public Image Thumbnail { get; set; }
        }

        public class EpisodeIDs : IDs
        {
            public int AniDB { get; set; }
            
            public List<int> TvDB { get; set; } = new List<int>();
        }
    }
}