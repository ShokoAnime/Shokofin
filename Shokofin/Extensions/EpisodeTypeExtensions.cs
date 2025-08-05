
using Shokofin.API.Models;

namespace Shokofin.Extensions;

public static class EpisodeTypeExtensions {
    public static string ToShortString(this EpisodeType episodeType)
        => episodeType switch {
            EpisodeType.Normal => "E",
            EpisodeType.Special => "SP",
            EpisodeType.Trailer => "T",
            EpisodeType.Other => "O",
            EpisodeType.ThemeSong => "C",
            EpisodeType.Parody => "P",
            _ => "?",
        };
}
