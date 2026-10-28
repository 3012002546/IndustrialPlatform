using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class StudioFoundationTests
{
    [Fact]
    public void Canonical_json_preserves_large_versions_decimal_strings_and_array_order()
    {
        var first = JsonNode.Parse("{\"version\":\"9007199254740993\",\"b\":[2,1],\"a\":{\"z\":1,\"x\":2}}");
        var second = JsonNode.Parse("{\"a\":{\"x\":2,\"z\":1},\"b\":[2,1],\"version\":\"9007199254740993\"}");
        Assert.Equal(StudioJson.Canonicalize(first), StudioJson.Canonicalize(second));
        Assert.Equal(9007199254740993L, StudioJson.Version("9007199254740993"));
        Assert.Equal(999999999999999999L, StudioJson.ScaledDecimal("999999999999.999999", "qty"));
        Assert.Throws<StudioException>(() => StudioJson.ScaledDecimal("1.0000001", "qty"));
        Assert.Throws<StudioException>(() => StudioJson.ScaledDecimal("", "qty"));
    }

    [Fact]
    public void External_identity_does_not_alias_case_tenant_or_issuer()
    {
        var id = StudioPrincipal.ExternalNId("mes", "t", "Alice");
        Assert.NotEqual(id, StudioPrincipal.ExternalNId("mes", "t", "alice"));
        Assert.NotEqual(id, StudioPrincipal.ExternalNId("mes", "other", "Alice"));
        Assert.NotEqual(id, StudioPrincipal.ExternalNId("other", "t", "Alice"));
        Assert.Throws<StudioException>(() => StudioPrincipal.ExternalNId("mes", "t\0x", "Alice"));
    }

    [Theory]
    [InlineData("Dashboard")]
    [InlineData("Report")]
    [InlineData("Traceability")]
    public void Product_install_does_not_require_lowcode(string module)
    {
        var profile = new StudioDeploymentProfile { Enabled = true, InstalledModules = [module], Jobs = new() { Provider = module == "Report" ? "Platform" : "Disabled" } };
        profile.Validate();
        Assert.True(profile.IsKindEnabled("Dataset"));
        Assert.False(profile.IsKindEnabled("LowCodePage"));
    }

    [Fact]
    public void Adapter_validation_never_falls_back_to_platform_or_local_admin()
    {
        var profile = new StudioDeploymentProfile { Enabled = true, DeploymentMode = "ExternalMes" };
        Assert.Throws<StudioException>(() => profile.Validate());
        profile = new() { Enabled = true, InstalledModules = ["LowCode"], EnabledModules = ["Report"] };
        Assert.Throws<StudioException>(() => profile.Validate());
    }

    [Theory]
    [InlineData("public,platform_studio")]
    [InlineData("$user")]
    [InlineData("pg_catalog")]
    [InlineData("x;drop table y")]
    public void Postgres_schema_cannot_be_search_path_or_sql(string schema) =>
        Assert.Throws<StudioException>(() => new StudioDatabaseOptions { Provider = "PostgreSQL", ConnectionStringRef = "test-ref", Schema = schema }.Validate());

    [Theory]
    [InlineData("__proto__")]
    [InlineData("constructor")]
    [InlineData("prototype")]
    public void Binding_rejects_unsafe_paths(string segment) =>
        Assert.Throws<StudioException>(() => StudioJson.ReadPath(JsonNode.Parse("{}"), segment));

    [Fact]
    public void ReadPath_supports_numeric_segments_for_array_results()
    {
        var result = StudioJson.ReadPath(JsonNode.Parse("[{\"source_time\":\"2026-10-22T00:00:00Z\"}]"), "0.source_time");

        Assert.Equal("2026-10-22T00:00:00Z", StudioJson.Text(result));
    }
}
