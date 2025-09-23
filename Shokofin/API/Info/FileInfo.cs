using System.Collections.Generic;
using System.Linq;
using Shokofin.API.Models;
using Shokofin.ExternalIds;

namespace Shokofin.API.Info;

public class FileInfo(File file, string seriesId, IReadOnlyList<(EpisodeInfo Episode, CrossReference.EpisodeCrossReferenceIDs CrossReference, string Id)> episodeList) {
    public string Id { get; init; } = file.Id.ToString();

    private string? _internalId;

    public string InternalId => _internalId ??= ShokoInternalId.FileNamespace + Id + $"?seriesId={SeriesId}&episodeIds={string.Join(",", EpisodeList.Select(tuple => tuple.Id))}&seasonId={EpisodeList.FirstOrDefault(tuple => tuple.Episode.SeasonId != null).Episode?.SeasonId ?? string.Empty}";

    public string SeriesId { get; init; } = seriesId;

    public MediaBrowser.Model.Entities.ExtraType? ExtraType { get; init; } = episodeList.FirstOrDefault(tuple => tuple.Episode.ExtraType != null).Episode?.ExtraType;

    public File Shoko { get; init; } = file;

    public IReadOnlyList<(EpisodeInfo Episode, CrossReference.EpisodeCrossReferenceIDs CrossReference, string Id)> EpisodeList { get; init; } = episodeList;
}
