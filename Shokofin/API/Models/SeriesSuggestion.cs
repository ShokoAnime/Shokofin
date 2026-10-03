namespace Shokofin.API.Models;

/// <summary>
/// A suggestion from one series to another, as provided by a source such as
/// AniDB, TMDB or a metadata plugin.
/// </summary>
public class SeriesSuggestion {
    /// <summary>
    /// The IDs of the series the suggestion is for.
    /// </summary>
    public SuggestionIDs IDs { get; set; } = new();

    /// <summary>
    /// The IDs of the suggested series.
    /// </summary>
    public SuggestionIDs SuggestedIDs { get; set; } = new();

    /// <summary>
    /// The kind of suggestion, either "Recommended" or "Similar".
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// The source key, e.g. "anidb", "tmdb" or a plugin provided key.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// The source's own rank, best first, starting at 0.
    /// </summary>
    public int? Order { get; set; }

    /// <summary>
    /// The approval rating as a percentage between 0 and 100, if the source
    /// provides one.
    /// </summary>
    public double? ApprovalRating { get; set; }

    /// <summary>
    /// The number of votes, if the source provides it.
    /// </summary>
    public int? Votes { get; set; }

    /// <summary>
    /// The source's net score, if the source provides it. Has no fixed range
    /// and may be negative.
    /// </summary>
    public int? Score { get; set; }

    /// <summary>
    /// Suggestion IDs.
    /// </summary>
    public class SuggestionIDs {
        /// <summary>
        /// The ID of the <see cref="Shoko.ShokoSeries"/> entry, if the entry
        /// is in the collection.
        /// </summary>
        public int? Shoko { get; set; }

        /// <summary>
        /// The ID of the <see cref="AniDB.AnidbAnime"/> entry.
        /// </summary>
        public int? AniDB { get; set; }

        /// <summary>
        /// The ID of the TMDB show entry.
        /// </summary>
        public int? TmdbShow { get; set; }

        /// <summary>
        /// The ID of the TMDB movie entry.
        /// </summary>
        public int? TmdbMovie { get; set; }

        /// <summary>
        /// The source's own ID, for sources other than AniDB and TMDB.
        /// </summary>
        public string? Provider { get; set; }
    }
}
