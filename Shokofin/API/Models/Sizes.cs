namespace Shokofin.API.Models
{
    public class Sizes
    {
        public EpisodeCounts Local { get; set; }
        
        public EpisodeCounts Watched { get; set; }
        
        public EpisodeCounts Total { get; set; }
        
        public class EpisodeCounts
        {
            public int Episodes { get; set; }
            
            public int Specials { get; set; }
            
            public int Credits { get; set; }
            
            public int Trailers { get; set; }
            
            public int Parodies { get; set; }
            
            public int Others { get; set; }
        }
    }
}