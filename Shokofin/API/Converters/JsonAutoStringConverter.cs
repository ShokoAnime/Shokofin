
using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shokofin.API.Converters;

/// <summary>
/// Automatically converts JSON values to a string.
/// </summary>
public class JsonAutoStringConverter : JsonConverter<string> {
    public override bool CanConvert(Type typeToConvert)
        => typeof(string) == typeToConvert;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType is JsonTokenType.Number) {
            if (reader.TryGetInt64(out var number))
                return number.ToString(CultureInfo.InvariantCulture);

            if (reader.TryGetDouble(out var doubleNumber))
                return doubleNumber.ToString(CultureInfo.InvariantCulture);
        }

        if (reader.TokenType is JsonTokenType.String)
            return reader.GetString();

        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.Clone().ToString();
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
