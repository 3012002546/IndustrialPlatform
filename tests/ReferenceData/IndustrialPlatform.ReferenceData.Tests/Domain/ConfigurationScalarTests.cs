using System.Text.Json;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.Parameter;

namespace IndustrialPlatform.ReferenceData.Tests.Domain;

public sealed class ConfigurationScalarTests
{
    [Theory]
    [InlineData(ReferenceDataType.String, "\"\"", "\"\"")]
    [InlineData(ReferenceDataType.Integer, "9223372036854775807", "9223372036854775807")]
    [InlineData(ReferenceDataType.Decimal, "123.4500000000", "123.45")]
    [InlineData(ReferenceDataType.Decimal, "1e-10", "0.0000000001")]
    [InlineData(ReferenceDataType.Boolean, "false", "false")]
    [InlineData(ReferenceDataType.Date, "\"2024-02-29\"", "\"2024-02-29\"")]
    [InlineData(ReferenceDataType.DateTime, "\"2024-02-29T08:00:00+08:00\"", "\"2024-02-29T00:00:00.0000000Z\"")]
    [InlineData(ReferenceDataType.Enum, "\"ready\"", "\"READY\"")]
    [InlineData(ReferenceDataType.Reference, "\"opaque-id\"", "\"opaque-id\"")]
    public void Nine_types_produce_typed_canonical_json(ReferenceDataType type, string input, string expected) =>
        Assert.Equal(expected, Parse(type, input).CanonicalValue);

    [Theory]
    [InlineData(ReferenceDataType.Integer, "9223372036854775808")]
    [InlineData(ReferenceDataType.Integer, "1.5")]
    [InlineData(ReferenceDataType.Decimal, "1000000000000000000")]
    [InlineData(ReferenceDataType.Decimal, "0.00000000001")]
    [InlineData(ReferenceDataType.Decimal, "0.123456789012345678901234567891")]
    [InlineData(ReferenceDataType.Decimal, "\"1.2\"")]
    [InlineData(ReferenceDataType.Boolean, "\"true\"")]
    [InlineData(ReferenceDataType.Boolean, "1")]
    [InlineData(ReferenceDataType.Date, "\"2023-02-29\"")]
    [InlineData(ReferenceDataType.DateTime, "\"2024-02-29T08:00:00\"")]
    [InlineData(ReferenceDataType.Json, "null")]
    [InlineData(ReferenceDataType.Reference, "\"\"")]
    public void Invalid_or_lossy_values_are_rejected(ReferenceDataType type, string input) =>
        Assert.Throws<ReferenceDataException>(() => Parse(type, input));

    [Fact]
    public void Canonical_hash_ignores_object_order_and_numeric_format_but_preserves_array_order()
    {
        var first = Parse(ReferenceDataType.Json, "{\"b\":[1,2],\"a\":1.00}");
        Assert.Equal(first.CanonicalValueHash, Parse(ReferenceDataType.Json, "{\"a\":1e0,\"b\":[1.0,2.00]}").CanonicalValueHash);
        Assert.NotEqual(first.CanonicalValueHash, Parse(ReferenceDataType.Json, "{\"a\":1,\"b\":[2,1]}").CanonicalValueHash);
        Assert.Equal(Parse(ReferenceDataType.Decimal, "1").CanonicalValueHash, Parse(ReferenceDataType.Decimal, "1.000").CanonicalValueHash);
    }

    [Fact]
    public void Size_limits_measure_utf8_and_errors_never_echo_sensitive_content()
    {
        Assert.Throws<ReferenceDataException>(() => Parse(ReferenceDataType.String, JsonSerializer.Serialize(new string('界', 1366))));
        var error = Assert.Throws<ReferenceDataException>(() => Parse(ReferenceDataType.String, "\"Password=never-print-this;Host=local\""));
        Assert.Equal("REF-CONFIG-SENSITIVE-REJECTED", error.ErrorCode);
        Assert.DoesNotContain("never-print-this", error.ToString());
        Assert.Throws<ReferenceDataException>(() => Parse(ReferenceDataType.Json, "{\"query\":\"SELECT * FROM users\"}"));
        Assert.Throws<ReferenceDataException>(() => Parse(ReferenceDataType.Json, "{\"template\":\"${run()}\"}"));
        Assert.Equal("REF-CONFIG-SENSITIVE-REJECTED", Assert.Throws<ReferenceDataException>(() => Parse(ReferenceDataType.String, "\"postgresql://user:private@localhost/db\"")).ErrorCode);
        Assert.Throws<ReferenceDataException>(() => Parse(ReferenceDataType.Json, "{\"number\":1e-20000}"));
    }

    internal static ConfigurationScalar Parse(ReferenceDataType type, string json) => ConfigurationScalar.Parse(type, JsonDocument.Parse(json).RootElement);
}
