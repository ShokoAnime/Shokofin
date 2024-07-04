using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

/// <summary>
/// Describes relations between two series entries.
/// </summary>
public class Relation
{
    /// <summary>
    /// The IDs of the series.
    /// </summary>
    public RelationIDs IDs { get; set; } = new();

    /// <summary>
    /// The IDs of the related series.
    /// </summary>
    public RelationIDs RelatedIDs { get; set; } = new();

    /// <summary>
    /// The relation between <see cref="Relation.IDs"/> and <see cref="Relation.RelatedIDs"/>.
    /// </summary>
    public RelationType Type { get; set; }

    /// <summary>
    /// AniDB, etc.
    /// </summary>
    public string Source { get; set; } = "Unknown";

    /// <summary>
    /// Relation IDs.
    /// </summary>
    public class RelationIDs
    {
        /// <summary>
        /// The ID of the <see cref="Series"/> entry.
        /// </summary>
        public int? Shoko { get; set; }

        /// <summary>
        /// The ID of the <see cref="Series.AniDB"/> entry.
        /// </summary>
        public int? AniDB { get; set; }
    }
}

/// <summary>
/// Explains how the main entry relates to the related entry.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RelationType
{
    /// <summary>
    /// The relation between the entries cannot be explained in simple terms.
    /// </summary>
    Other = 0,

    /// <summary>
    /// The entries use the same setting, but follow different stories.
    /// </summary>
    SameSetting = 1,

    /// <summary>
    /// The entries use the same base story, but is set in alternate settings.
    /// </summary>
    AlternativeSetting = 2,

    /// <summary>
    /// The entries tell the same story in the same settings but are made at different times.
    /// </summary>
    AlternativeVersion = 3,

    /// <summary>
    /// The entries tell different stories in different settings but otherwise shares some character(s).
    /// </summary>
    SharedCharacters = 4,

    /// <summary>
    /// The first story either continues, or expands upon the story of the related entry.
    /// </summary>
    Prequel = 20,

    /// <summary>
    /// The related entry is the main-story for the main entry, which is a side-story.
    /// </summary>
    MainStory = 21,

    /// <summary>
    /// The related entry is a longer version of the summarized events in the main entry.
    /// </summary>
    FullStory = 22,

    /// <summary>
    /// The related entry either continues, or expands upon the story of the main entry.
    /// </summary>
    Sequel = 40,

    /// <summary>
    /// The related entry is a side-story for the main entry, which is the main-story.
    /// </summary>
    SideStory = 41,

    /// <summary>
    /// The related entry summarizes the events of the story in the main entry.
    /// </summary>
    Summary = 42,
}

/// <summary>
/// Extensions related to relations
/// </summary>
public static class RelationExtensions
{
    /// <summary>
    /// Reverse the relation.
    /// </summary>
    /// <param name="type">The relation to reverse.</param>
    /// <returns>The reversed relation.</returns>
    public static RelationType Reverse(this RelationType type)
    {
        return type switch
        {
            RelationType.Prequel => RelationType.Sequel,
            RelationType.Sequel => RelationType.Prequel,
            RelationType.MainStory => RelationType.SideStory,
            RelationType.SideStory => RelationType.MainStory,
            RelationType.FullStory => RelationType.Summary,
            RelationType.Summary => RelationType.FullStory,
            _ => type
        };
    }
}
