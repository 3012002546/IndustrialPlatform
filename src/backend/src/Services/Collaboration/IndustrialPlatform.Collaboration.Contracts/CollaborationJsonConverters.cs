using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IndustrialPlatform.Collaboration.Contracts;

/// <summary>
/// PF05 keeps sequence and version values lossless at the browser boundary. The CLR
/// contract remains numeric for arithmetic and persistence, while JSON uses canonical
/// decimal strings and accepts only the same representation on input.
/// </summary>
public sealed class CollaborationInt64StringConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var number) && number >= 0)
            return number;
        if (reader.TokenType == JsonTokenType.String
            && long.TryParse(reader.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            && parsed >= 0)
            return parsed;
        throw new JsonException("Expected a non-negative Int64 decimal value.");
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}

public sealed class CollaborationNullableInt64StringConverter : JsonConverter<long?>
{
    private readonly CollaborationInt64StringConverter _inner = new();

    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : _inner.Read(ref reader, typeof(long), options);

    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else _inner.Write(writer, value.Value, options);
    }
}

public sealed class CollaborationInt32StringConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number) && number >= 0)
            return number;
        if (reader.TokenType == JsonTokenType.String
            && int.TryParse(reader.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            && parsed >= 0)
            return parsed;
        throw new JsonException("Expected a non-negative Int32 decimal value.");
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}
