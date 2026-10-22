using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Data;
using IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using IndustrialPlatform.Querying.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class StudioSourceCatalogTests
{
    [Fact]
    public async Task Sqlite_catalog_reads_real_metadata_for_only_public_registered_fields_and_audits_access()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var file = Path.Combine(Path.GetTempPath(), "studio-catalog-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var raw = new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ConnectionString;
            await WriteAsync(raw, "CREATE TABLE inventory(n_id TEXT PRIMARY KEY,qty TEXT,hidden_secret TEXT DEFAULT 'never-disclose-this'); CREATE TABLE unapproved(password TEXT)");
            var endpoint = new StudioSourceEndpoint { Provider = "Sqlite", CatalogKey = "public-inventory", ConnectionStringRef = "source-secret-ref", ReadOnlyVerified = true, AllowedObjects = ["inventory"] };
            fixture.Profile.SourceRegistry["inventory"] = endpoint;
            using var http = new HttpClient();
            using var executor = new RegisteredSourceExecutor(fixture.Profile, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["source-secret-ref"] = raw }).Build(), http, TimeProvider.System);
            var admin = new SourceAdministration(fixture.Store, fixture.Authorization, fixture.Commands, executor, fixture.Profile, new(), TimeProvider.System);
            var created = await admin.CreateAsync(fixture.Principal, new("catalog-source", "公开目录", null, "Sqlite", "inventory", "public-inventory", new JsonArray(new JsonObject { ["kind"] = "Table", ["qualifiedName"] = "inventory" })), "create", CancellationToken.None);
            var schema = Schema();
            var reader = new RegisteredSourceCatalog(executor, [new("inventory", "inventory", "Table", "studio.dataset.design", schema), new("inventory", "unapproved", "Table", "studio.dataset.design", Schema())]);
            var catalog = new SourceCatalog(fixture.Store, fixture.Authorization, admin, reader, TimeProvider.System);
            var result = await catalog.GetAsync(fixture.Principal, created.NId, created.ConfigurationVersion, CancellationToken.None);
            var item = Assert.Single(result.Objects);
            Assert.Equal("inventory", item.QualifiedName);
            Assert.Equal(["n_id", "qty"], item.Columns.Select(column => column.Name));
            Assert.True(item.Columns[0].PrimaryKey); Assert.False(item.Columns[0].Nullable);
            Assert.Equal("TEXT", item.Columns[1].DatabaseType);
            var json = JsonSerializer.Serialize(result);
            Assert.DoesNotContain("hidden_secret", json, StringComparison.Ordinal);
            Assert.DoesNotContain("never-disclose", json, StringComparison.Ordinal);
            Assert.DoesNotContain("source-secret-ref", json, StringComparison.Ordinal);
            Assert.DoesNotContain(file, json, StringComparison.Ordinal);
            Assert.True(await fixture.Database.Client.Ado.GetIntAsync("SELECT COUNT(*) FROM studio_audit_outbox WHERE payload_json LIKE '%studio.source.catalog%'") > 0);
            Assert.Equal(403, (await Assert.ThrowsAsync<StudioException>(() => catalog.GetAsync(fixture.Principal with { Permissions = ["studio.source.view"] }, created.NId, created.ConfigurationVersion, CancellationToken.None))).StatusCode);
            Assert.Equal("SOURCE_CHANGED", (await Assert.ThrowsAsync<StudioException>(() => catalog.GetAsync(fixture.Principal, created.NId, "2", CancellationToken.None))).Code);
            await WriteAsync(raw, "ALTER TABLE inventory DROP COLUMN qty");
            Assert.Equal("SOURCE_CHANGED", (await Assert.ThrowsAsync<StudioException>(() => catalog.GetAsync(fixture.Principal, created.NId, created.ConfigurationVersion, CancellationToken.None))).Code);
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public async Task Registered_api_and_native_querying_catalogs_never_fetch_business_data_or_disclose_private_fields()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var endpoint = new StudioSourceEndpoint { BaseAddress = "https://no-network.invalid/private", CredentialRef = "secret-ref", ReadOnlyVerified = true, Operations = new() { ["public-query"] = new() { Path = "/never-called" }, ["hidden-query"] = new() { Path = "/never-called" }, ["mutating"] = new() { Path = "/never-called", Method = "POST", ReadOnly = false } } };
        fixture.Profile.SourceRegistry["api"] = endpoint;
        using var http = new HttpClient(); using var executor = new RegisteredSourceExecutor(fixture.Profile, new ConfigurationBuilder().Build(), http, TimeProvider.System);
        var source = new StudioSource { NId = "native-public", Provider = "RegisteredApi", EndpointKey = "api", EndpointConfigurationHash = executor.ConfigurationHash(endpoint), AllowedObjectsJson = "[{\"kind\":\"ApiOperation\",\"qualifiedName\":\"public-query\"},{\"kind\":\"ApiOperation\",\"qualifiedName\":\"hidden-query\"},{\"kind\":\"ApiOperation\",\"qualifiedName\":\"mutating\"}]" };
        var definition = new QueryResourceDefinition("public-records", new Dictionary<string, QueryFieldDefinition> { ["n_id"] = QueryFieldDefinition.Text(), ["hidden_secret"] = QueryFieldDefinition.Text(selectable: false) });
        var registration = RegisteredSourceCatalog.FromQuerying("api", "public-query", "studio.dataset.design", definition) with { Parameters = new JsonArray(new JsonObject { ["name"] = "keyword", ["type"] = "String", ["defaultValue"] = "private-default" }, new JsonObject { ["name"] = "tenant", ["type"] = "String", ["source"] = "TrustedContext" }) };
        var reader = new RegisteredSourceCatalog(executor, [registration, new("api", "hidden-query", "ApiOperation", "ungranted.permission", Schema()), new("api", "mutating", "ApiOperation", "studio.dataset.design", Schema())]);
        var result = await reader.ReadAsync(fixture.Principal, source, CancellationToken.None);
        Assert.Single(result);
        Assert.Equal("keyword", Assert.Single(result[0].Parameters)!["name"]!.GetValue<string>());
        var json = JsonSerializer.Serialize(result);
        foreach (var hidden in new[] { "hidden_secret", "private-default", "no-network.invalid", "secret-ref", "ungranted", "mutating" }) Assert.DoesNotContain(hidden, json, StringComparison.Ordinal);
    }

    [Fact]
    public void Sqlserver_chinese_dictionary_covers_all_core_and_framework_columns_without_fallback_labels()
    {
        var tables = StudioSchema.Core.Concat([StudioSqlServerDescriptions.MigrationLedger, StudioSqlServerDescriptions.SeedLedger]).ToArray();
        Assert.Equal(18, tables.Length);
        foreach (var table in tables)
        {
            var statements = StudioSqlServerDescriptions.Statements(table);
            Assert.Equal(table.Columns.Split(',').Length + 1, statements.Count);
            Assert.Contains(StudioSqlServerDescriptions.TableDescription(table.Name), statements[0], StringComparison.Ordinal);
            foreach (var column in table.Columns.Split(',').Select(spec => spec.Split(':')[0]))
            {
                var description = StudioSqlServerDescriptions.ColumnDescription(column);
                Assert.Contains(description, statements.Single(statement => statement.Contains("@level2name=N'" + column + "'", StringComparison.Ordinal)), StringComparison.Ordinal);
                Assert.Contains(description, character => character is >= '\u4e00' and <= '\u9fff');
            }
        }
    }

    private static JsonObject Schema() => new() { ["id"] = "root", ["name"] = "rows", ["type"] = "Array", ["item"] = new JsonObject { ["id"] = "row", ["name"] = "row", ["type"] = "Object", ["children"] = new JsonArray(new JsonObject { ["id"] = "nId", ["name"] = "n_id", ["type"] = "String", ["nullable"] = false }, new JsonObject { ["id"] = "quantity", ["name"] = "qty", ["type"] = "Decimal", ["nullable"] = true }, new JsonObject { ["id"] = "secret", ["name"] = "hidden_secret", ["type"] = "String", ["nullable"] = true, ["sensitive"] = true }) } };
    private static async Task WriteAsync(string connectionString, string sql)
    {
        await using var connection = new SqliteConnection(connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
    }
}
