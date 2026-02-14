
using Shokofin.API.Models;

namespace Shokofin.Extensions;

public static class EpisodeTypeExtensions {
    public static string ToShortString(this EpisodeType episodeType)
        => episodeType switch {
            EpisodeType.Episode => "E",
            EpisodeType.Special => "SP",
            EpisodeType.Trailer => "T",
            EpisodeType.Other => "O",
            EpisodeType.Credits => "C",
            EpisodeType.Parody => "P",
            _ => "?",
        };
}
