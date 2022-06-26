namespace Shokofin.API.Models
{
    public class Group : BaseModel
    {
        public GroupIDs IDs { get; set; }

        public string SortName { get; set; }

        public string Description { get; set; }

        public bool HasCustomName { get; set; }

        public class GroupIDs : IDs
        {
            public int? DefaultSeries { get; set; }

            public int? ParentGroup { get; set; }

            public int TopLevelGroup { get; set; }
        }
    }
}
