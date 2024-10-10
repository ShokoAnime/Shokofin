using System.Collections.Generic;

namespace Shokofin.API.Models;

public class Images
{
    public List<Image> Posters { get; set; } = [];

    public List<Image> Backdrops { get; set; } = [];

    // Backwards compatibility with stable 4.2.2.0 server.
    public List<Image> Fanarts
    {
        get => Backdrops;
        set => Backdrops = value;
    }

    public List<Image> Banners { get; set; } = [];

    public List<Image> Logos { get; set; } = [];
}

public class EpisodeImages : Images
{
    public List<Image> Thumbnails { get; set; } = [];
}