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

    public List<(EpisodeInfo Episode, CrossReference.EpisodeCrossReferenceIDs CrossReference, string Id)> EpisodeList;

    public List<List<(EpisodeInfo Episode, CrossReference.EpisodeCrossReferenceIDs CrossReference, string Id)>> AlternateEpisodeLists;

    public FileInfo(File file, List<List<(EpisodeInfo Episode, CrossReference.EpisodeCrossReferenceIDs CrossReference, string Id)>> groupedEpisodeLists, string seriesId)
    {
        var episodeList = groupedEpisodeLists.FirstOrDefault() ?? [];
        var alternateEpisodeLists = groupedEpisodeLists.Count > 1 ? groupedEpisodeLists.GetRange(1, groupedEpisodeLists.Count - 1) : [];
        Id = file.Id.ToString();
        SeriesId = seriesId;
        ExtraType = episodeList.FirstOrDefault(tuple => tuple.Episode.ExtraType != null).Episode?.ExtraType;
        Shoko = file;
        EpisodeList = episodeList;
        AlternateEpisodeLists = alternateEpisodeLists;
    }
}
