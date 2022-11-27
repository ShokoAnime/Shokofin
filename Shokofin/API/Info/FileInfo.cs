using System.Collections.Generic;
using System.Linq;
using Shokofin.API.Models;

#nullable enable
namespace Shokofin.API.Info;

public class FileInfo
{
    public string Id;

    public string SeriesId;

    public MediaBrowser.Model.Entities.ExtraType? ExtraType;

    public File File;

    public List<EpisodeInfo> EpisodeList;

    public FileInfo(File file, List<EpisodeInfo> episodeList, string seriesId)
    {
        Id = file.Id.ToString();
        SeriesId = seriesId;
        ExtraType = episodeList.FirstOrDefault(episode => episode.ExtraType != null)?.ExtraType;
        File = file;
        EpisodeList = episodeList;
    }
}
