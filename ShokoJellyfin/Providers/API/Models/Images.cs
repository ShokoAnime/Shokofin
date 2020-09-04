using System.Collections.Generic;

namespace ShokoJellyfin.Providers.API.Models
{
    public class Images
    {
        public List<Image> Posters { get; set; } = new List<Image>();
        
        public List<Image> Fanarts { get; set; } = new List<Image>();
        
        public List<Image> Banners { get; set; } = new List<Image>();
        
    }
}