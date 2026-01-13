
namespace Shokofin.MergeVersions;

/// <summary>
/// Defines how versions of the same video are sorted when merged in the UI.
/// </summary>
public enum MergeVersionSortSelector {
    /// <summary>
    /// Versions are sorted using the import date of the file.
    /// </summary>
    ImportedAt = 1,

    /// <summary>
    /// Versions are sorted using the creation date of the file.
    /// </summary>
    CreatedAt = 2,

    /// <summary>
    /// Versions are sorted by the resolution of the video, with the highest resolution first.
    /// </summary>
    Resolution = 3,

    /// <summary>
    /// Versions are sorted alphabetically based on the release group name.
    /// </summary>
    ReleaseGroupName = 4,

    /// <summary>
    /// Versions are sorted using the file source (e.g. Blu-Ray, Web, DVD, etc...)
    /// </summary>
    FileSource = 5,

    /// <summary>
    /// Versions are sorted using the file version, if available.
    /// </summary>
    FileVersion = 6,

    /// <summary>
    /// Versions are sorted using the relative folder depth. Deeper files last.
    /// </summary>
    RelativeDepth = 7,

    /// <summary>
    /// Versions are sorted so files not marked as a variation come before files
    /// marked as a variation.
    /// </summary>
    NoVariation = 8,
}
