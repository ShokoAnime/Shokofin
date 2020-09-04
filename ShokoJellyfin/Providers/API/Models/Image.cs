namespace ShokoJellyfin.Providers.API.Models
{
    public class Image
    {
        public string Source { get; set; }
        
        public string Type { get; set; }
        
        public string ID { get; set; }
        
        public string RelativeFilepath { get; set; }
        
        public bool Preferred { get; set; }
        
        public bool Disabled { get; set; }
        
    }
}