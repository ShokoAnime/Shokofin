using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shokofin.API.Converters;

/// <summary>
/// Converts a partial date (yyyy, yyyy-MM, yyyy-MM-dd) or full date-time to a <see cref="DateTime"/> instance.
/// </summary>
public class JsonPartialDateTimeConverter : JsonConverter<DateTime?> {
    public override bool CanConvert(Type typeToConvert)
        => typeof(DateTime?) == typeToConvert || typeof(DateTime) == typeToConvert;

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType is JsonTokenType.String &&
        reader.GetString() is { Length: >= 4 } value &&
        (
            DateTime.TryParseExact(value, "yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime) ||
            DateTime.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime) ||
            DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime) ||
            DateTime.TryParse(value, CultureInfo.InvariantCulture, out dateTime)
        ) &&
        !(
            dateTime == DateTime.MinValue ||
            dateTime == DateTime.MaxValue
        )
            ? dateTime
            : null;

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options) {
        if (value.HasValue)
            writer.WriteStringValue(value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        else
            writer.WriteNullValue();
    }
}