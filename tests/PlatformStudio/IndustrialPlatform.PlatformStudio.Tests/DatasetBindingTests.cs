using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class DatasetBindingTests
{
    [Fact]
    public async Task Real_readonly_sqlite_query_respects_parameters_factory_scope_and_column_projection()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var sourceFile = Path.Combine(Path.GetTempPath(), "studio-source-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using (var writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourceFile, Pooling = false }.ConnectionString))
            {
                await writer.OpenAsync();
                await using var command = writer.CreateCommand();
                command.CommandText = "CREATE TABLE work_orders(n_id TEXT,line_code TEXT,qty TEXT,factory_n_id TEXT,hidden TEXT); INSERT INTO work_orders VALUES ('WO1','L01','1.100000','factory-a','secret-a'),('WO2','L02','2.200000','factory-a','secret-b'),('WO3','L02','3.300000','factory-b','secret-c');";
                await command.ExecuteNonQueryAsync();
            }
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Sources:readonly"] = new SqliteConnectionStringBuilder { DataSource = sourceFile, Pooling = false }.ConnectionString }).Build();
            var endpoint = new StudioSourceEndpoint { Provider = "Sqlite", CatalogKey = "fixture", ConnectionStringRef = "Sources:readonly", ReadOnlyVerified = true, AllowedObjects = ["work_orders"], FactoryFieldPath = ["factory_n_id"] };
            fixture.Profile.SourceRegistry["fixture-db"] = endpoint;
            using var http = new HttpClient();
            using var executor = new RegisteredSourceExecutor(fixture.Profile, configuration, http, TimeProvider.System);
            var admin = new SourceAdministration(fixture.Store, fixture.Authorization, fixture.Commands, executor, fixture.Profile, new(), TimeProvider.System);
            var source = await admin.CreateAsync(fixture.Principal, new("source-a", "隔离只读源", null, "Sqlite", "fixture-db", "fixture", new JsonArray(new JsonObject { ["kind"] = "View", ["qualifiedName"] = "work_orders" })), "source-create", CancellationToken.None);
            await admin.TestAsync(fixture.Principal, source.NId, new(source.OptimisticVersion, source.ConcurrencyVersion), CancellationToken.None);
            source = await admin.SetEnabledAsync(fixture.Principal, source.NId, true, new(source.OptimisticVersion, source.ConcurrencyVersion), CancellationToken.None);
            var document = Document(source.NId, source.ConfigurationVersion);
            var service = new DatasetExecution(fixture.Store, fixture.Publishing, fixture.Authorization, executor, new(), TimeProvider.System);
            var missing = await Assert.ThrowsAsync<StudioException>(() => service.PreviewAsync(fixture.Principal, new(source.NId, source.ConfigurationVersion, document, new()), CancellationToken.None));
            Assert.Equal("PARAM_REQUIRED", missing.Code);
            var result = await service.PreviewAsync(fixture.Principal, new(source.NId, source.ConfigurationVersion, document, new() { ["lineCode"] = "L02" }), CancellationToken.None);
            var rows = result["data"]!.AsArray();
            Assert.Single(rows);
            Assert.Equal("WO2", rows[0]!["n_id"]!.GetValue<string>());
            Assert.Equal("2.200000", rows[0]!["qty"]!.GetValue<string>());
            Assert.Null(rows[0]!["hidden"]);
            Assert.Null(rows[0]!["factory_n_id"]);
            Assert.Equal("Unknown", result["quality"]!["status"]!.GetValue<string>());
            var unsafeDocument = document.DeepClone().AsObject();
            unsafeDocument["query"]!["statement"] = "DELETE FROM work_orders";
            var refused = await Assert.ThrowsAsync<StudioException>(() => service.PreviewAsync(fixture.Principal, new(source.NId, source.ConfigurationVersion, unsafeDocument, new() { ["lineCode"] = "L02" }), CancellationToken.None));
            Assert.Equal("STUDIO_FORBIDDEN", refused.Code);
            await using var verify = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourceFile, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ConnectionString);
            await verify.OpenAsync(); await using var count = verify.CreateCommand(); count.CommandText = "SELECT COUNT(*) FROM work_orders";
            Assert.Equal(3L, Convert.ToInt64(await count.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture));
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(sourceFile)) File.Delete(sourceFile); }
    }
    [Fact]
    public async Task Missing_or_wrong_parameters_cause_zero_executor_calls()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var execution = new DatasetExecution(fixture.Store, fixture.Publishing, fixture.Authorization, fixture.SourceExecutor, new(), TimeProvider.System);
        var doc = Document("not-needed", "1");
        await Assert.ThrowsAsync<StudioException>(() => execution.PreviewAsync(fixture.Principal, new("not-needed", "1", doc, new()), CancellationToken.None));
        await Assert.ThrowsAsync<StudioException>(() => execution.PreviewAsync(fixture.Principal, new("not-needed", "1", doc, new() { ["lineCode"] = 123 }), CancellationToken.None));
        Assert.Equal(0, fixture.SourceExecutor.Calls);
    }
    internal static JsonObject Document(string source, string version) => new()
    {
        ["schemaVersion"] = 1, ["sourceNId"] = source, ["sourceConfigurationVersion"] = version,
        ["query"] = new JsonObject { ["kind"] = "Sql", ["statement"] = "SELECT n_id,line_code,qty,factory_n_id,hidden FROM work_orders WHERE line_code=@lineCode ORDER BY n_id", ["parameters"] = new JsonArray("lineCode") },
        ["parameters"] = new JsonArray(new JsonObject { ["name"] = "lineCode", ["label"] = "产线", ["type"] = "String", ["required"] = true, ["nullable"] = false, ["source"] = "Caller" }),
        ["result"] = new JsonObject { ["id"] = "root", ["name"] = "rows", ["type"] = "Array", ["nullable"] = false, ["item"] = new JsonObject { ["id"] = "row", ["name"] = "row", ["type"] = "Object", ["nullable"] = false, ["children"] = new JsonArray(new JsonObject { ["id"] = "nId", ["name"] = "n_id", ["label"] = "工单编号", ["type"] = "String", ["nullable"] = false }, new JsonObject { ["id"] = "qty", ["name"] = "qty", ["label"] = "数量", ["type"] = "Decimal", ["nullable"] = false }, new JsonObject { ["id"] = "hidden", ["name"] = "hidden", ["type"] = "String", ["nullable"] = false }) } },
        ["rowPolicyKey"] = "authorized-factories", ["readableFieldIds"] = new JsonArray("nId", "qty"),
        ["timeoutSeconds"] = 30, ["maxRows"] = 10000, ["maxBytes"] = "10485760", ["cacheSeconds"] = 0,
        ["freshness"] = new JsonObject { ["mode"] = "AcquisitionOnly" },
    };
}
