using System.Collections.Generic;

namespace Shokofin.API.Models;

public class Images
{
    public List<Image> Posters { get; set; } = new List<Image>();

    public List<Image> Backdrops { get; set; } = new List<Image>();

    public List<Image> Banners { get; set; } = new List<Image>();
}
