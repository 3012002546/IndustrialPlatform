using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class StudioSourceExecutionTests
{
    [Theory]
    [InlineData("SELECT * FROM inventory; DELETE FROM inventory")]
    [InlineData("SELECT * INTO leak FROM inventory")]
    [InlineData("SELECT * FROM unapproved")]
    [InlineData("SELECT writefile('x','y')")]
    [InlineData("WITH x AS (DELETE FROM inventory RETURNING *) SELECT * FROM x")]
    [InlineData("SELECT dbo.hidden_side_effect() FROM inventory")]
    public void Unsafe_queries_are_rejected_before_opening_a_connection(string query) => Assert.Throws<StudioException>(() => SqlReadOnlyPolicy.Validate(query, ["inventory"]));
    [Theory]
    [InlineData("SELECT code,qty FROM inventory WHERE code=@code")]
    [InlineData("WITH x AS (SELECT code FROM inventory) SELECT * FROM x")]
    [InlineData("SELECT COUNT(*) FROM inventory")]
    public void Registered_read_only_queries_are_allowed(string query) => Assert.Equal(query, SqlReadOnlyPolicy.Validate(query, ["inventory"]));
    [Theory]
    [InlineData("CREATE PROCEDURE p AS INSERT INTO inventory VALUES (1)")]
    [InlineData("CREATE PROCEDURE p AS EXEC('DELETE FROM inventory')")]
    [InlineData("CREATE PROCEDURE p AS BEGIN UPDATE inventory SET qty=0 END")]
    public void Mutating_procedure_definitions_cannot_be_admitted(string definition) => Assert.Throws<StudioException>(() => SqlReadOnlyPolicy.VerifyProcedureDefinition(definition));
    [Fact]
    public void Caller_cannot_override_trusted_factory_or_tenant_parameters()
    {
        var principal = new StudioPrincipal("tenant-a", "u", "test", "u", [], [], ["factory-a"], "1", "1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
        var doc = new JsonObject { ["parameters"] = new JsonArray(new JsonObject { ["name"] = "tenant", ["type"] = "String", ["required"] = true, ["source"] = "TrustedContext", ["contextKey"] = "tenant" }) };
        var actual = DatasetValues.Parameters(principal, doc, new() { ["tenant"] = "tenant-b" });
        Assert.Equal("tenant-a", actual["tenant"]!.GetValue<string>());
    }
}
