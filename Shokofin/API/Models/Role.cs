using System;
using System.Text.Json.Serialization;

namespace Shokofin.API.Models;

public class Role : IEquatable<Role>
{
    /// <summary>
    /// Extra info about the role. For example, role can be voice actor, while role_details is Main Character
    /// </summary>
    [JsonPropertyName("RoleDetails")]
    public string Name { get; set; } = string.Empty;

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

    public override bool Equals(object? obj)
        => Equals(obj as Role);

    public bool Equals(Role? other)
    {
        if (other is null)
            return false;

        return string.Equals(Name, other.Name, StringComparison.Ordinal) &&
            Type == other.Type &&
            string.Equals(Language, other.Language, StringComparison.Ordinal) &&
            Staff.Equals(other.Staff) &&
            (Character is null ? other.Character is null : Character.Equals(other.Character));
    }

    public override int GetHashCode()
    {
        var hash = 17;

        hash = hash * 31 + (Name?.GetHashCode() ?? 0);
        hash = hash * 31 + Type.GetHashCode();
        hash = hash * 31 + (Language?.GetHashCode() ?? 0);
        hash = hash * 31 + Staff.GetHashCode();
        hash = hash * 31 + (Character?.GetHashCode() ?? 0);

        return hash;
    }

    public class Person : IEquatable<Person>
    {
        /// <summary>
        /// Main Name, romanized if needed
        /// ex. John Smith
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Alternate Name, this can be any other name, whether kanji, an alias, etc
        /// ex. 澤野弘之
        /// </summary>
        public string? AlternateName { get; set; }

        /// <summary>
        /// A description, bio, etc
        /// ex. John Smith was born September 12, 1980 in Tokyo, Japan. He is a composer and arranger.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Visual representation of the character or staff. Usually a profile
        /// picture.
        /// </summary>
        public Image Image { get; set; } = new();

        public override bool Equals(object? obj)
            => Equals(obj as Person);

        public bool Equals(Person? other)
        {
            if (other is null)
                return false;

            return string.Equals(Name, other.Name, StringComparison.Ordinal) &&
                string.Equals(Description, other.Description, StringComparison.Ordinal) &&
                string.Equals(AlternateName, other.AlternateName, StringComparison.Ordinal);
        }

        public override int GetHashCode()
        {
            var hash = 17;

            hash = hash * 31 + (Name?.GetHashCode() ?? 0);
            hash = hash * 31 + (AlternateName?.GetHashCode() ?? 0);
            hash = hash * 31 + (Description?.GetHashCode() ?? 0);

            return hash;
        }
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
    /// Responsible for the creation of the source work this show is derived from.
    /// </summary>
    SourceWork,
}
