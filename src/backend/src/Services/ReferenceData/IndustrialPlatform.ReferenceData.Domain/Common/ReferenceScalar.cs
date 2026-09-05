using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IndustrialPlatform.ReferenceData.Domain.Common;

#pragma warning disable CA1720 // V2.7 type names are domain vocabulary.
public enum ReferenceDataType { String, Integer, Decimal, Boolean, Date, DateTime, Enum, Json, Reference }
#pragma warning restore CA1720
public sealed record ReferenceScalar
{
    public ReferenceDataType DataType { get; }
    public string CanonicalValue { get; }
    public string CanonicalValueHash => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalValue)));
    public JsonElement JsonValue { get { using var document = JsonDocument.Parse(CanonicalValue); return document.RootElement.Clone(); } }

    private ReferenceScalar(ReferenceDataType dataType, string value) { DataType = dataType; CanonicalValue = value; }

    public static ReferenceScalar Parse(ReferenceDataType type, JsonElement value)
    {
        if (!Enum.IsDefined(type) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) throw Invalid();
        if (Encoding.UTF8.GetByteCount(value.GetRawText()) > 65536) throw Invalid();

        string canonical;
        switch (type)
        {
            case ReferenceDataType.String:
                canonical = JsonSerializer.Serialize(String(value, 4096, allowEmpty: true, bytes: true));
                break;
            case ReferenceDataType.Integer:
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var integer)) throw Invalid();
                canonical = integer.ToString(CultureInfo.InvariantCulture);
                break;
            case ReferenceDataType.Decimal:
                if (value.ValueKind != JsonValueKind.Number) throw Invalid();
                var (digits, exponent, negative) = NumberParts(value.GetRawText());
                if (digits != "0" && (exponent < -10 || digits.Length + exponent > 18)) throw Invalid();
                canonical = FixedNumber(digits, exponent, negative);
                break;
            case ReferenceDataType.Boolean:
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid();
                canonical = value.GetBoolean() ? "true" : "false";
                break;
            case ReferenceDataType.Date:
                var dateText = String(value, 10);
                if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) throw Invalid();
                canonical = JsonSerializer.Serialize(dateText);
                break;
            case ReferenceDataType.DateTime:
                var dateTimeText = String(value, 40);
                if (!Regex.IsMatch(dateTimeText, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)
                    || !DateTimeOffset.TryParse(dateTimeText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime)) throw Invalid();
                // PostgreSQL timestamptz stores microseconds; normalize before hashing so both providers expose the same value.
                var utcTicks = dateTime.UtcTicks - dateTime.UtcTicks % 10;
                canonical = JsonSerializer.Serialize(new DateTime(utcTicks, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture));
                break;
            case ReferenceDataType.Enum:
                canonical = JsonSerializer.Serialize(ReferenceValidation.NId(String(value, 64), item: true));
                break;
            case ReferenceDataType.Reference:
                canonical = JsonSerializer.Serialize(String(value, 128));
                break;
            case ReferenceDataType.Json:
                using (var stream = new MemoryStream())
                {
                    using (var writer = new Utf8JsonWriter(stream)) WriteJson(writer, value);
                    canonical = Encoding.UTF8.GetString(stream.ToArray());
                }
                if (Encoding.UTF8.GetByteCount(canonical) > 65536) throw Invalid();
                break;
            default: throw Invalid();
        }
        return new(type, canonical);
    }

    private static string String(JsonElement value, int maximum, bool allowEmpty = false, bool bytes = false)
    {
        if (value.ValueKind != JsonValueKind.String) throw Invalid();
        var text = value.GetString()!;
        if (text.Contains('\0') || (!allowEmpty && string.IsNullOrWhiteSpace(text)) || (bytes ? Encoding.UTF8.GetByteCount(text) : text.Length) > maximum) throw Invalid();
        return text;
    }

    // Work on decimal digits directly: decimal.TryParse can silently round excess precision.
    private static (string Digits, int Exponent, bool Negative) NumberParts(string raw)
    {
        var negative = raw.StartsWith('-');
        var pieces = raw.TrimStart('-').Split(['e', 'E']);
        var exponent = 0;
        if (pieces.Length == 2 && (!int.TryParse(pieces[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent) || Math.Abs((long)exponent) > 65536)) throw Invalid();
        var dot = pieces[0].IndexOf('.');
        if (dot >= 0) exponent -= pieces[0].Length - dot - 1;
        var digits = pieces[0].Replace(".", "").TrimStart('0');
        if (digits.Length == 0) return ("0", 0, false);
        var trimmed = digits.TrimEnd('0');
        exponent += digits.Length - trimmed.Length;
        return (trimmed, exponent, negative);
    }

    private static string FixedNumber(string digits, int exponent, bool negative)
    {
        var point = digits.Length + exponent;
        var result = exponent >= 0 ? digits + new string('0', exponent)
            : point > 0 ? digits.Insert(point, ".") : "0." + new string('0', -point) + digits;
        return negative ? "-" + result : result;
    }

    private static void WriteJson(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var properties = value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
                if (properties.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length) throw Invalid();
                foreach (var property in properties)
                {
                    if (property.Name.Contains('\0')) throw Invalid();
                    writer.WritePropertyName(property.Name); WriteJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteJson(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                var (digits, exponent, negative) = NumberParts(value.GetRawText());
                // PostgreSQL jsonb uses numeric: at most 131072 integer and 16383 fractional digits.
                if (digits.Length + exponent > 131072 || exponent < -16383) throw Invalid();
                writer.WriteRawValue((negative ? "-" : "") + digits + (exponent == 0 ? "" : "e" + exponent.ToString(CultureInfo.InvariantCulture)));
                break;
            case JsonValueKind.String:
                var text = value.GetString()!;
                if (text.Contains('\0')) throw Invalid();
                if (Regex.IsMatch(text, @"<script\b|javascript:|\$\{|\{\{|\b(SELECT\b.+\bFROM|INSERT\s+INTO|UPDATE\b.+\bSET|DELETE\s+FROM|DROP\s+TABLE|eval\s*\()", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)) throw Invalid();
                writer.WriteStringValue(text);
                break;
            default: value.WriteTo(writer); break;
        }
    }

    private static ReferenceDataException Invalid() => new("REF-VALIDATION-FAILED", field: "value");
}
