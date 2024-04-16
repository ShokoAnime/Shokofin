using System.Collections.Generic;
using System.Linq;
using Shokofin.API.Models;

namespace Shokofin.API.Info;

public class FileInfo
{
    public string Id;

    public string SeriesId;

    public MediaBrowser.Model.Entities.ExtraType? ExtraType;

    public File Shoko;

    public List<EpisodeInfo> EpisodeList;

    public List<List<EpisodeInfo>> AlternateEpisodeLists;

    public FileInfo(File file, List<List<EpisodeInfo>> groupedEpisodeLists, string seriesId)
    {
        var episodeList = groupedEpisodeLists.FirstOrDefault() ?? new();
        var alternateEpisodeLists = groupedEpisodeLists.Count > 1 ? groupedEpisodeLists.GetRange(1, groupedEpisodeLists.Count - 1) : new();
        Id = file.Id.ToString();
        SeriesId = seriesId;
        ExtraType = episodeList.FirstOrDefault(episode => episode.ExtraType != null)?.ExtraType;
        Shoko = file;
        EpisodeList = episodeList;
        AlternateEpisodeLists = alternateEpisodeLists;
    }
}
