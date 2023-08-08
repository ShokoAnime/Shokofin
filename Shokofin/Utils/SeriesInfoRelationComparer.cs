using System;
using System.Collections.Generic;
using System.Linq;
using Shokofin.API.Info;
using Shokofin.API.Models;

#nullable enable
namespace Shokofin.Utils;

public class SeriesInfoRelationComparer : IComparer<SeasonInfo>
{
    protected static Dictionary<RelationType, int> RelationPriority = new() {
        { RelationType.Prequel, 1 },
        { RelationType.MainStory, 2 },
        { RelationType.FullStory, 3 },

        { RelationType.AlternativeVersion, 21 },
        { RelationType.SameSetting, 22 },
        { RelationType.AlternativeSetting, 23 },

        { RelationType.SideStory, 41 },
        { RelationType.Summary, 42 },
        { RelationType.Sequel, 43 },

        { RelationType.SharedCharacters, 99 },
    };

    public int Compare(SeasonInfo? a, SeasonInfo? b)
    {
        // Check for `null` since `IComparer<T>` expects `T` to be nullable.
        if (a == null && b == null)
            return 0;
        if (a == null)
            return 1;
        if (b == null)
            return -1;

        // Check for direct relations.
        var directRelationComparison = CompareDirectRelations(a, b);
        if (directRelationComparison != 0)
            return directRelationComparison;

        // Check for indirect relations.
        var indirectRelationComparison = CompareIndirectRelations(a, b);
        if (indirectRelationComparison != 0)
            return indirectRelationComparison;

        // Fallback to checking the air dates if they're not indirectly related
        // or if they have the same relations.
        return CompareAirDates(a.AniDB.AirDate, b.AniDB.AirDate);
    }

    private int CompareDirectRelations(SeasonInfo a, SeasonInfo b)
    {
        // We check from both sides because one of the entries may be outdated,
        // so the relation may only present on one of the entries.
        if (a.RelationMap.TryGetValue(b.Id, out var relationType))
            if (relationType == RelationType.Prequel || relationType == RelationType.MainStory)
                return -1;
            else if (relationType == RelationType.Sequel || relationType == RelationType.SideStory)
                return 1;

        if (b.RelationMap.TryGetValue(a.Id, out relationType))
            if (relationType == RelationType.Prequel || relationType == RelationType.MainStory)
                return 1;
            else if (relationType == RelationType.Sequel || relationType == RelationType.SideStory)
                return -1;

        // The entries are not considered to be directly related.
        return 0;
    }
    
    private int CompareIndirectRelations(SeasonInfo a, SeasonInfo b)
    {
        var xRelations = a.Relations
            .Where(r => RelationPriority.ContainsKey(r.Type))
            .Select(r => r.Type)
            .OrderBy(r => RelationPriority[r])
            .ToList();
        var yRelations = b.Relations
            .Where(r => RelationPriority.ContainsKey(r.Type))
            .Select(r => r.Type)
            .OrderBy(r => RelationPriority[r])
            .ToList();
        for (int i = 0; i < Math.Max(xRelations.Count, yRelations.Count); i++) {
            // The first entry have overall less relations, so it comes after the second entry.
            if (i >= xRelations.Count)
                return 1;
            // The second entry have overall less relations, so it comes after the first entry.
            else if (i >= yRelations.Count)
                return -1;

            // Compare the relation priority to see which have a higher priority.
            var xRelationType = xRelations[i];
            var xRelationPriority = RelationPriority[xRelationType];
            var yRelationType = yRelations[i];
            var yRelationPriority = RelationPriority[yRelationType];
            var relationPriorityComparison = xRelationPriority.CompareTo(yRelationPriority);
            if (relationPriorityComparison != 0)
                return relationPriorityComparison;
        }

        // The entries are not considered to be indirectly related, or they have
        // the same relations.
        return 0;
    }

    private int CompareAirDates(DateTime? a, DateTime? b)
    {
        return a.HasValue ? b.HasValue ? DateTime.Compare(a.Value, b.Value) : 1 : b.HasValue ? -1 : 0;
    }
}
