
namespace Shokofin.API.Info.TMDB;

public class TmdbSeasonInfo {
    public required string TmdbShowId { get; init; }

    public required string TmdbAlternateOrderingId { get; init; }

    public required string TmdbSeasonId { get; init; }

    public required int SeasonNumber { get; init; }

    public bool UsesAlternateOrdering => TmdbShowId != TmdbAlternateOrderingId;

    public TmdbShowInfo ToShowInfo() => new() {
        TmdbShowId = TmdbShowId,
        TmdbAlternateOrderingId = TmdbAlternateOrderingId,
    };
}
