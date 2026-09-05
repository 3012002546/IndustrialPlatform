using System.Text.Json;
using System.Text.RegularExpressions;
using IndustrialPlatform.ReferenceData.Domain.Common;

namespace IndustrialPlatform.ReferenceData.Domain.Parameter;

#pragma warning disable CA1720 // V2.7 public value-mode names are domain vocabulary.
public enum ConfigurationValueMode { Single, Multi }
#pragma warning restore CA1720
public enum ConfigurationStatus { Active, Disabled }

/// <summary>Parameter-specific sensitive-data rejection around the shared typed value.</summary>
public sealed record ConfigurationScalar
{
    private readonly ReferenceScalar scalar;
    private ConfigurationScalar(ReferenceScalar value) { scalar = value; }
    public ReferenceDataType DataType => scalar.DataType;
    public string CanonicalValue => scalar.CanonicalValue;
    public string CanonicalValueHash => scalar.CanonicalValueHash;
    public JsonElement JsonValue => scalar.JsonValue;
    public static ConfigurationScalar Parse(ReferenceDataType type, JsonElement value)
    {
        ParameterValidation.RejectSensitiveValue(value);
        return new(ReferenceScalar.Parse(type, value));
    }
    public static ConfigurationScalar? Optional(ReferenceDataType type, JsonElement? value) =>
        value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : Parse(type, value.Value);
}
public static partial class ParameterValidation
{
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{1,63}$")]
    private static partial Regex IdentifierPattern();
    [GeneratedRegex(@"password|passwd|pwd|access[_-]?token|refresh[_-]?token|id[_-]?token|client[_-]?secret|idp[_-]?secret|private[_-]?key|connection[_-]?string|api[_-]?key|^token$|^secret$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveNamePattern();
    [GeneratedRegex(@"-----BEGIN (?:[A-Z ]+ )?PRIVATE KEY-----|(?:password|passwd|pwd|client_secret|access_token)\s*[=:]|\bBearer\s+\S+|\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+|(?:Host|Server|Data Source)\s*=[^;]+;|[a-z][a-z0-9+.-]*://[^\s/@]+:[^\s/@]+@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveValuePattern();

    public static string NId(string value)
    {
        if (value is null || !IdentifierPattern().IsMatch(value)) throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "nId");
        RejectSensitiveName(value);
        return value.ToUpperInvariant();
    }

    public static void RejectSensitiveName(string value)
    {
        if (SensitiveNamePattern().IsMatch(value)) throw Sensitive();
    }

    public static void RejectSensitiveValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String && SensitiveValuePattern().IsMatch(value.GetString()!)) throw Sensitive();
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject()) { RejectSensitiveName(property.Name); RejectSensitiveValue(property.Value); }
        if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectSensitiveValue(item);
    }

    public static string Reason(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 500) throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "changeReason");
        if (SensitiveValuePattern().IsMatch(value)) throw Sensitive();
        return value.Trim();
    }

    private static ReferenceDataException Sensitive() => new("REF-CONFIG-SENSITIVE-REJECTED");
}
