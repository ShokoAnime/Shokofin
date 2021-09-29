using System.Text.Json.Serialization;

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
        
        [JsonIgnore]
        public virtual string Path
            => $"/api/v3/Image/{Source}/{Type}/{ID}";
        
        public string ToURLString()
        {
            return string.Concat(Plugin.Instance.Configuration.Host, Path);
        }
    }
}