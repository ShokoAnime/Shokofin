
namespace Shokofin.Configuration;

/// <summary>
/// Image language types.
/// </summary>
public enum ImageLanguageType {
    /// <summary>
    /// The language is unknown.
    /// </summary>
    Unknown = -1,

    /// <summary>
    /// No language / Text-less images.
    /// </summary>
    None = 0,

    /// <summary>
    /// Follow metadata language in library.
    /// </summary>
    Metadata = 1,

    /// <summary>
    /// Use the language from the media's country of origin.
    /// </summary>
    Original = 2,

    /// <summary>
    /// English.
    /// </summary>
    English = 3,
}
