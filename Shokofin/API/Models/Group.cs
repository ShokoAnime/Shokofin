#nullable enable
namespace Shokofin.API.Models;

public class Group
{
    public string Name { get; set; } = string.Empty;

    public int Size { get; set; }

    public GroupIDs IDs { get; set; } = new();

    public string SortName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool HasCustomName { get; set; }

    /// <summary>
    /// Sizes object, has totals
    /// </summary>
    public GroupSizes Sizes { get; set; } = new();

    public class GroupIDs : IDs
    {
        public int MainSeries { get; set; }

        public int? ParentGroup { get; set; }

        public int TopLevelGroup { get; set; }
    }

    /// <summary>
    /// Downloaded, Watched, Total, etc
    /// </summary>
    public class GroupSizes : Series.SeriesSizes
    {
        /// <summary>
        /// Number of direct sub-groups within the group.
        /// /// </summary>
        /// <value></value>
        public int SubGroups { get; set; }

        /// <summary>
        /// Count of the different series types within the group.
        /// </summary>
        public SeriesTypeCounts SeriesTypes { get; set; } = new();

        public class SeriesTypeCounts
        {
            public int Unknown;
            public int Other;
            public int TV;
            public int TVSpecial;
            public int Web;
            public int Movie;
            public int OVA;
        }
    }
}
