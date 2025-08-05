
using System.Diagnostics.CodeAnalysis;

namespace Shokofin.API.Info.TMDB;

public class TmdbEpisodeInfo {
    public required string TmdbShowId { get; init; }
    
    public required string TmdbAlternateOrderingId { get; init; }

    public required string TmdbSeasonId { get; init; }

    public required string TmdbEpisodeId { get; init; }

    public required string? TvdbEpisodeId { get; init; }

    public required int SeasonNumber { get; init; }

    public required int EpisodeNumber { get; init; }

    public required int? OriginalEpisodeNumber { get; init; }

    public required int? OriginalSeasonNumber { get; init; }

    [MemberNotNullWhen(true, nameof(OriginalEpisodeNumber))]
    [MemberNotNullWhen(true, nameof(OriginalSeasonNumber))]
    public bool UsesAlternateOrdering => TmdbShowId != TmdbAlternateOrderingId;
}
