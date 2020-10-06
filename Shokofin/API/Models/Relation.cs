using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Shokofin.API.Models
{
    /// <summary>
    /// Describes relations between series.
    /// </summary>
    public class Relation
    {
        /// <summary>
        /// Relation from ID
        /// </summary>
        public int FromID { get; set; }

        /// <summary>
        /// Relation to ID
        /// </summary>
        public int ToID { get; set; }

        /// <summary>
        /// The relation between `FromID` and `ToID`
        /// </summary>
        [Required]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RelationType Type { get; set; }

        /// <summary>
        /// If the relation is valid both ways, or if the relation is only valid one way
        /// </summary>
        /// <value></value>
        [Required]
        public bool IsBiDirectional { get; set; }

        /// <summary>
        /// AniDB, etc
        /// </summary>
        [Required]
        public string Source { get; set; }



        /// <summary>
        /// Explains how the first entry relates to the second entry.
        /// </summary>
        public enum RelationType
        {
            /// <summary>
            /// The relation between the entries cannot be explained in simple terms.
            /// </summary>
            Other = 1,

            /// <summary>
            /// The entries use the same setting, but follow different stories.
            /// </summary>
            SameSetting = 2,

            /// <summary>
            /// The entries use the same base story, but is set in alternate settings.
            /// </summary>
            AlternativeSetting = 3,

            /// <summary>
            /// The entries tell different stories but shares some character(s).
            /// </summary>
            SharedCharacters = 4,

            /// <summary>
            /// The entries tell the same story, with their differences.
            /// </summary>
            AlternativeVersion = 5,

            /// <summary>
            /// The second entry either continues, or expands upon the story of the first entry.
            /// </summary>
            Sequel = 50,

            /// <summary>
            /// The second entry is a side-story for the first entry, which is the main-story.
            /// </summary>
            SideStory = 51,

            /// <summary>
            /// The second entry summerizes the events of the story in the first entry.
            /// </summary>
            Summary = 52,

            /// <summary>
            /// The second entry is a later production of the story in the first story, often 
            /// </summary>
            Reboot = 53,
        }
    }
}