using System.Text.Json.Serialization;

#nullable enable
namespace Shokofin.API.Models;

public class Role
{
    /// <summary>
    /// Extra info about the role. For example, role can be voice actor, while role_details is Main Character
    /// </summary>
    [JsonPropertyName("RoleDetails")]
    public string Name { get; set; } = "";

    /// <summary>
    /// The role that the staff plays, cv, writer, director, etc
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [JsonPropertyName("RoleName")]
    public CreatorRoleType Type { get; set; }

    /// <summary>
    /// Most will be Japanese. Once AniList is in, it will have multiple options
    /// </summary>
    public string? Language { get; set; }

    public Person Staff { get; set; } = new();

    /// <summary>
    /// The character played, the <see cref="Role.Type"/> is of type
    /// <see cref="CreatorRoleType.Seiyuu"/>.
    /// </summary>
    public Person? Character { get; set; }

    public class Person
    {
        /// <summary>
        /// Main Name, romanized if needed
        /// ex. Sawano Hiroyuki
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Alternate Name, this can be any other name, whether kanji, an alias, etc
        /// ex. 澤野弘之
        /// </summary>
        public string? AlternateName { get; set; }

        /// <summary>
        /// A description, bio, etc
        /// ex. Sawano Hiroyuki was born September 12, 1980 in Tokyo, Japan. He is a composer and arranger.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Visual representation of the character or staff. Usually a profile
        /// picture.
        /// </summary>
        public Image Image { get; set; } = new();
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
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
