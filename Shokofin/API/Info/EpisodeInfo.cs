using System.Linq;
using Shokofin.API.Models;
using Shokofin.Utils;

#nullable enable
namespace Shokofin.API.Info;

public class EpisodeInfo
{
    public string Id;

    public MediaBrowser.Model.Entities.ExtraType? ExtraType;

    public Episode Shoko;

    public Episode.AniDB AniDB;

    public Episode.TvDB? TvDB;

    public EpisodeInfo(Episode episode)
    {
        Id = episode.IDs.Shoko.ToString();
        ExtraType = Ordering.GetExtraType(episode.AniDBEntity);
        Shoko = episode;
        AniDB = episode.AniDBEntity;
        TvDB = episode.TvDBEntityList?.FirstOrDefault();
    }
}
