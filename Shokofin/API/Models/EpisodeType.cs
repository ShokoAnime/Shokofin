using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EpisodeType {
    /// <summary>
    /// A catch-all type for future extensions when a provider can't use a current episode type, but knows what the future type should be.
    /// </summary>
    Other = 2,

    /// <summary>
    /// The episode type is unknown.
    /// </summary>
    Unknown = Other,

    /// <summary>
    /// A normal episode.
    /// </summary>
    Normal = 1,

    /// <summary>
    /// A special episode.
    /// </summary>
    Special = 3,

    /// <summary>
    /// A trailer.
    /// </summary>
    Trailer = 4,

    /// <summary>
    /// Either an opening-song, or an ending-song.
    /// </summary>
    ThemeSong = 5,

    /// <summary>
    /// Intro, and/or opening-song.
    /// </summary>
    OpeningSong = 6,

    /// <summary>
    /// Outro, end-roll, credits, and/or ending-song.
    /// </summary>
    EndingSong = 7,

    /// <summary>
    /// AniDB parody type. Where else would this be useful?
    /// </summary>
    Parody = 8,

    /// <summary>
    /// A interview tied to the series.
    /// </summary>
    Interview = 9,

    /// <summary>
    /// A DVD or BD extra, e.g. BD-menu or deleted scenes.
    /// </summary>
    Extra = 10,
}
