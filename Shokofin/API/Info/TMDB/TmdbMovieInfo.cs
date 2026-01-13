
using System;

namespace Shokofin.API.Info.TMDB;

public class TmdbMovieInfo : IComparable<TmdbMovieInfo>, IEquatable<TmdbMovieInfo> {
    public required string TmdbMovieId { get; init; }

    public required string? TmdbMovieCollectionId { get; init; }

    public int CompareTo(TmdbMovieInfo? other)
        => other is null ? 1 : TmdbMovieId.CompareTo(other?.TmdbMovieId);

    public bool Equals(TmdbMovieInfo? other)
        => other is not null && (ReferenceEquals(this, other) || TmdbMovieId == other.TmdbMovieId);

    public override bool Equals(object? obj)
        => Equals(obj as TmdbMovieInfo);

    public override int GetHashCode()
        => HashCode.Combine(TmdbMovieId);
}
