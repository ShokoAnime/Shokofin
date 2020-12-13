namespace Shokofin.API.Models
{
    public class Image
    {
        public string Source { get; set; }
        
        public string Type { get; set; }
        
        public string ID { get; set; }
        
        public string RelativeFilepath { get; set; }
        
        public bool Preferred { get; set; }
        
        public bool Disabled { get; set; }
        
        public string ToURLString()
        {
            return $"{Plugin.Instance.Configuration.Host}/api/v3/Image/{Source}/{Type}/{ID}";
        }
    }
}