using System.Linq;
using Shokofin.API.Models;
using Shokofin.Utils;

using SpecialOrderType = Shokofin.Utils.Ordering.SpecialOrderType;

#nullable enable
namespace Shokofin.API.Info;

public class EpisodeInfo
{
    public string Id;

    public MediaBrowser.Model.Entities.ExtraType? ExtraType;

    public Episode Shoko;

    public Episode.AniDB AniDB;

    public Episode.TvDB? TvDB;

    public bool IsSpecial
    {
        get
        {
            var order = Plugin.Instance.Configuration.SpecialsPlacement;
            var allowOtherData = order == SpecialOrderType.InBetweenSeasonByOtherData || order == SpecialOrderType.InBetweenSeasonMixed;
            return allowOtherData  ? (TvDB?.SeasonNumber == 0 || AniDB.Type == EpisodeType.Special) : AniDB.Type == EpisodeType.Special;
        }
    }

    public EpisodeInfo(Episode episode)
    {
        Id = episode.IDs.Shoko.ToString();
        ExtraType = Ordering.GetExtraType(episode.AniDBEntity);
        Shoko = episode;
        AniDB = episode.AniDBEntity;
        TvDB = episode.TvDBEntityList?.FirstOrDefault();
    }
}
