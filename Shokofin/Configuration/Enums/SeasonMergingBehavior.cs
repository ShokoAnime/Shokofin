using System;

namespace Shokofin.Configuration;

/// <summary>
/// Determine how to handle series merging.
/// </summary>
[Flags]
public enum SeasonMergingBehavior {
    /// <summary>
    /// Follow the global series merging settings.
    /// </summary>
    None = 0,

    /// <summary>
    /// Do not merge this series with any other.
    /// </summary>
    NoMerge = 1 << 0,

    /// <summary>
    /// Attempt to merge this series with the sequel series.
    /// </summary>
    MergeForward = 1 << 1,

    /// <summary>
    /// Attempt to merge this series with the prequel series.
    /// </summary>
    MergeBackward = 1 << 2,

    /// <summary>
    /// Attempt to merge this series with the main story series.
    /// </summary>
    MergeWithMainStory = 1 << 3,

    /// <summary>
    /// Attempt to merge this series with one or more other series in merge
    /// group A. All other series that should be merged into this series need to
    /// have <see cref="MergeGroupASource"/> set.
    /// </summary>
    MergeGroupATarget = 1 << 4,

    /// <summary>
    /// Attempt to merge this series into another series in merge group A. The
    /// other series needs to have <see cref="MergeGroupATarget"/> set.
    /// </summary>
    MergeGroupASource = 1 << 5,

    /// <summary>
    /// Attempt to merge this series with one or more other series in merge
    /// group B. All other series that should be merged into this series need to
    /// have <see cref="MergeGroupBSource"/> set.
    /// </summary>
    MergeGroupBTarget = 1 << 6,

    /// <summary>
    /// Attempt to merge this series into another series in merge group B. The
    /// other series needs to have <see cref="MergeGroupBTarget"/> set.
    /// </summary>
    MergeGroupBSource = 1 << 7,

    /// <summary>
    /// Attempt to merge this series with one or more other series in merge
    /// group C. All other series that should be merged into this series need to
    /// have <see cref="MergeGroupCSource"/> set.
    /// </summary>
    MergeGroupCTarget = 1 << 8,

    /// <summary>
    /// Attempt to merge this series into another series in merge group C. The
    /// other series needs to have <see cref="MergeGroupCTarget"/> set.
    /// </summary>
    MergeGroupCSource = 1 << 9,

    /// <summary>
    /// Attempt to merge this series with one or more other series in merge
    /// group D. All other series that should be merged into this series need to
    /// have <see cref="MergeGroupDSource"/> set.
    /// </summary>
    MergeGroupDTarget = 1 << 10,

    /// <summary>
    /// Attempt to merge this series into another series in merge group D. The
    /// other series needs to have <see cref="MergeGroupDTarget"/> set.
    /// </summary>
    MergeGroupDSource = 1 << 11,
}
