
using System;
using System.Text.Json.Serialization;

namespace Shokofin.API.Models.Shoko;

public class ShokoGroup {
    public string Id => IDs.Shoko.ToString();

    public GroupIDs IDs { get; set; } = new();

    public string Name { get; set; } = string.Empty;

#if DEBUG
    public string SortName { get; set; } = string.Empty;

    public bool HasCustomName { get; set; }
#endif

    public string Description { get; set; } = string.Empty;

    public bool HasCustomDescription { get; set; }

    public int Size { get; set; }

    public GroupSizes Sizes { get; set; } = new();

    /// <summary>
    /// When the group entry was created.
    /// </summary>
    [JsonPropertyName("Created")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the group entry was last updated.
    /// </summary>
    [JsonPropertyName("Updated")]
    public DateTime LastUpdatedAt { get; set; }

    public class GroupIDs : IDs {
        public int MainSeries { get; set; }

        public int? ParentGroup { get; set; }

        public int TopLevelGroup { get; set; }
    }

    /// <summary>
    /// Downloaded, Watched, Total, etc
    /// </summary>
    public class GroupSizes : ShokoSeries.SeriesSizes {
        /// <summary>
        /// Number of direct sub-groups within the group.
        /// /// </summary>
        /// <value></value>
        public int SubGroups { get; set; }

#if DEBUG
        /// <summary>
        /// Count of the different series types within the group.
        /// </summary>
        public SeriesTypeCounts SeriesTypes { get; set; } = new();

        public class SeriesTypeCounts {
            public int Unknown { get; set; }
            public int Other { get; set; }
            public int TV { get; set; }
            public int TVSpecial { get; set; }
            public int Web { get; set; }
            public int Movie { get; set; }
            public int OVA { get; set; }
        }
#endif
    }
}
