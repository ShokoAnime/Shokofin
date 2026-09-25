using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shokofin.Events.Interfaces;

namespace Shokofin.API.Converters;

/// <summary>
/// Reads a metadata source into a <see cref="ProviderName"/>, from either the
/// enum name ("AniDB", "TMDB", "Shoko"), the kebab-case source value
/// ("anidb", "tmdb", "shoko") or the enum number. Any other source maps to
/// <see cref="ProviderName.None"/> instead of failing the whole event.
/// </summary>
public class JsonProviderNameConverter : JsonConverter<ProviderName> {
    public override ProviderName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType is JsonTokenType.Number)
            return reader.TryGetInt32(out var number) && Enum.IsDefined(typeof(ProviderName), number) ? (ProviderName)number : ProviderName.None;

        if (reader.TokenType is not JsonTokenType.String)
            throw new JsonException($"Unexpected token {reader.TokenType} when reading a metadata source.");

        var value = reader.GetString()?.Replace("-", string.Empty).Replace("_", string.Empty);
        return !string.IsNullOrEmpty(value) && Enum.TryParse<ProviderName>(value, ignoreCase: true, out var providerName) && Enum.IsDefined(providerName)
            ? providerName
            : ProviderName.None;
    }

    public override void Write(Utf8JsonWriter writer, ProviderName value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
