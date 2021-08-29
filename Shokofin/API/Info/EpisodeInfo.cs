using Shokofin.API.Models;

namespace Shokofin.API.Info
{
    public class EpisodeInfo
    {
        public string Id;

        public MediaBrowser.Model.Entities.ExtraType? ExtraType;

        public Episode Shoko;

        public Episode.AniDB AniDB;

        public Episode.TvDB TvDB;
    }
}
