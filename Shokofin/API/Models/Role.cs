using System.Text.Json.Serialization;

namespace Shokofin.API.Models
{
    public class Role
    {
        public string Language { get; set; }
        
        public Person Staff { get; set; }
        
        public Person Character { get; set; }
    
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public CreatorRoleType RoleName { get; set; }
        
        public string RoleDetails { get; set; }

        public class Person
        {
            public string Name { get; set; }
            
            public string AlternateName { get; set; }
            
            public string Description { get; set; }
            
            public Image Image { get; set; }
        }

        public enum CreatorRoleType
        {
            /// <summary>
            /// Voice actor or voice actress.
            /// </summary>
            Seiyuu,

            /// <summary>
            /// This can be anything involved in writing the show.
            /// </summary>
            Staff,

            /// <summary>
            /// The studio responsible for publishing the show.
            /// </summary>
            Studio,

            /// <summary>
            /// The main producer(s) for the show.
            /// </summary>
            Producer,

            /// <summary>
            /// Direction.
            /// </summary>
            Director,

            /// <summary>
            /// Series Composition.
            /// </summary>
            SeriesComposer,

            /// <summary>
            /// Character Design.
            /// </summary>
            CharacterDesign,

            /// <summary>
            /// Music composer.
            /// </summary>
            Music,

            /// <summary>
            /// Responsible for the creation of the source work this show is detrived from.
            /// </summary>
            SourceWork,
        }
    }
}