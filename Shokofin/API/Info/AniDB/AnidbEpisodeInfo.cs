using Shokofin.API.Models;
using Shokofin.Extensions;

namespace Shokofin.API.Info.AniDB;

public class AnidbEpisodeInfo {
    public required string AnidbEpisodeId { get; init; }

    public required string AnidbAnimeId { get; init; }

    public required int EpisodeNumber { get; init; }

    public required EpisodeType Type { get; init; }

    public string GetEpisodeNumberText() => Type.ToShortString() + EpisodeNumber;
}