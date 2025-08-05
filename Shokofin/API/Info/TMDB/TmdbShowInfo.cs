
using System;

namespace Shokofin.API.Info.TMDB;

public class TmdbShowInfo : IComparable<TmdbShowInfo>, IEquatable<TmdbShowInfo> {
    public required string TmdbShowId { get; init; }

    public required string TmdbAlternateOrderingId { get; init; }

    public string? TvdbShowId { get; init; }

    public bool UsesAlternateOrdering => TmdbShowId != TmdbAlternateOrderingId;

    public int CompareTo(TmdbShowInfo? other)
        => other is null ? 1 : TmdbShowId.CompareTo(other?.TmdbShowId);

    public bool Equals(TmdbShowInfo? other)
        => other is not null && (ReferenceEquals(this, other) || TmdbShowId == other.TmdbShowId);

    public override bool Equals(object? obj)
        => Equals(obj as TmdbShowInfo);

    public override int GetHashCode()
        => HashCode.Combine(TmdbShowId);
}
