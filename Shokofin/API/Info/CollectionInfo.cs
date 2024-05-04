
using System.Collections.Generic;
using Shokofin.API.Models;

namespace Shokofin.API.Info;

public class CollectionInfo
{
    public string Id;

    public string? ParentId;

    public bool IsTopLevel;

    public string Name;

    public Group Shoko;

    public IReadOnlyList<ShowInfo> Shows;

    public IReadOnlyList<CollectionInfo> SubCollections;

    public CollectionInfo(Group group, List<ShowInfo> shows, List<CollectionInfo> subCollections)
    {
        Id = group.IDs.Shoko.ToString();
        ParentId = group.IDs.ParentGroup?.ToString();
        IsTopLevel = group.IDs.TopLevelGroup == group.IDs.Shoko;
        Name = group.Name;
        Shoko = group;
        Shows = shows;
        SubCollections = subCollections;
    }
}
