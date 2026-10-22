using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Data;
using IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class StudioDataHardeningTests
{
    [Fact]
    public async Task Registered_api_refuses_redirect_before_requesting_the_target()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var address = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/";
        var requests = 0;
        var server = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(stop.Token);
                    Interlocked.Increment(ref requests);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    while (await reader.ReadLineAsync(stop.Token) is { Length: > 0 }) { }
                    var response = Encoding.ASCII.GetBytes("HTTP/1.1 302 Found\r\nLocation: " + address + "private-target\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(response, stop.Token);
                }
            }
            catch (OperationCanceledException) { }
        }, stop.Token);
        try
        {
            var profile = StudioInitializationTests.Profile("unused.db");
            var endpoint = new StudioSourceEndpoint { BaseAddress = address, ReadOnlyVerified = true, PublicQueriesAllowed = true, Operations = new() { ["read"] = new() { Path = "read" } } };
            profile.SourceRegistry["redirect"] = endpoint;
            using var http = new HttpClient();
            using var executor = new RegisteredSourceExecutor(profile, new ConfigurationBuilder().Build(), http, TimeProvider.System, true);
            var source = new StudioSource { NId = "redirect", Provider = "RegisteredApi", EndpointKey = "redirect", EndpointConfigurationHash = executor.ConfigurationHash(endpoint), AllowedObjectsJson = "[{\"qualifiedName\":\"read\",\"kind\":\"ApiOperation\"}]", MaxRows = 100, MaxBytes = 1000, TimeoutSeconds = 5 };
            var document = new JsonObject { ["rowPolicyKey"] = "public", ["query"] = new JsonObject { ["kind"] = "Api", ["operationKey"] = "read" } };
            var principal = new StudioPrincipal("tenant", "user", "test", "user", [], [], [], "1", "1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
            Assert.Equal("SOURCE_FAILED", (await Assert.ThrowsAsync<StudioException>(() => executor.ExecuteAsync(principal, source, document, new(), false, stop.Token))).Code);
            Assert.Equal(1, Volatile.Read(ref requests));
        }
        finally { await stop.CancelAsync(); listener.Stop(); await server; }
    }

    [Theory]
    [InlineData("WITH x(code) AS (SELECT code FROM inventory) SELECT * FROM x")]
    [InlineData("WITH x(a) AS (SELECT code FROM inventory), y(b) AS (SELECT a FROM x) SELECT * FROM y")]
    [InlineData("SELECT * FROM (SELECT * FROM inventory) x WHERE EXISTS (SELECT 1 FROM inventory)")]
    [InlineData("WITH RECURSIVE x(code) AS (SELECT code FROM inventory) SELECT * FROM x")]
    public void Readonly_cte_columns_and_subqueries_are_valid(string sql) => Assert.Equal(sql, SqlReadOnlyPolicy.Validate(sql, ["inventory"]));

    [Theory]
    [InlineData("CREATE PROCEDURE p AS SELECT dbo.hidden_side_effect()")]
    [InlineData("CREATE PROCEDURE p AS SELECT dbo . hidden_side_effect()")]
    [InlineData("CREATE PROCEDURE p AS SELECT NEXT VALUE FOR sequence_a")]
    [InlineData("CREATE PROCEDURE p AS SELECT * INTO leak FROM inventory")]
    public void Procedure_admission_checks_functions_and_sequence_side_effects(string sql) => Assert.Throws<StudioException>(() => SqlReadOnlyPolicy.VerifyProcedureDefinition(sql));

    [Theory]
    [InlineData("SELECT * FROM inventory, unapproved")]
    [InlineData("SELECT * FROM inventory a JOIN inventory b ON a.id=b.id, unapproved")]
    public void Implicit_joins_cannot_bypass_object_whitelist(string sql) => Assert.Throws<StudioException>(() => SqlReadOnlyPolicy.Validate(sql, ["inventory"]));

    [Fact]
    public void Result_schema_rejects_nonobject_children_and_scalar_substructures()
    {
        var document = DatasetBindingTests.Document("s", "1");
        document["result"]!["item"]!["children"]!.AsArray().Add(3);
        Assert.Contains(StudioDocuments.Validate("Dataset", document), item => item.Code == "TYPE_MISMATCH");
        Assert.Throws<StudioException>(() => DatasetValues.Project(new JsonArray(), document["result"]!.AsObject(), new HashSet<string> { "nId" }));
        document = DatasetBindingTests.Document("s", "1");
        document["result"]!["item"]!["children"]![0]!["children"] = new JsonArray();
        Assert.Contains(StudioDocuments.Validate("Dataset", document), item => item.Code == "TYPE_MISMATCH");
    }

    [Fact]
    public void Decimal_values_must_be_exactly_representable_before_database_binding()
    {
        DatasetValues.CheckScalar("Decimal", JsonValue.Create("12345678901234567890.123456")!, "value", new());
        DatasetValues.CheckScalar("Decimal", JsonValue.Create("+000.1000000000000000000000000000000000")!, "value", new());
        Assert.Throws<StudioException>(() => DatasetValues.CheckScalar("Decimal", JsonValue.Create("0.12345678901234567890123456789")!, "value", new()));
    }

    [Fact]
    public void Date_formatter_accepts_registered_Iana_zone_and_empty_binding_path()
    {
        var binding = new JsonObject { ["from"] = "Root", ["path"] = new JsonArray(), ["nullPolicy"] = "Error", ["transform"] = new JsonObject { ["kind"] = "DateFormat", ["args"] = new JsonObject { ["pattern"] = "yyyy-MM-dd HH:mm:ss", ["timeZone"] = "Asia/Taipei" } } };
        Assert.Equal("2026-10-22 08:00:00", StudioBinding.Resolve(binding, new Dictionary<string, JsonNode?> { ["Root"] = JsonValue.Create("2026-10-22T00:00:00Z") })!.GetValue<string>());
        Assert.Throws<StudioException>(() => StudioBinding.Resolve(binding, new Dictionary<string, JsonNode?> { ["Root"] = JsonValue.Create("2026-10-22T00:00:00") }));
    }

    [Fact]
    public async Task Rule_numbering_is_not_publishable_without_a_real_number_provider()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var model = StudioPublishingTests.Model(); model["numbering"]!["mode"] = "Rule"; model["numbering"]!["ruleKey"] = "mes-code";
        var created = await fixture.Publishing.CreateAsync(fixture.Principal, new("rule-model", "LedgerModel", "规则编号", null, null, model), "create", CancellationToken.None);
        var validation = await fixture.Publishing.ValidateAsync(fixture.Principal, created.NId, model, CancellationToken.None);
        Assert.False(validation.Valid);
        Assert.Contains(validation.Diagnostics, item => item.Path == "numbering.mode");
    }

    [Fact]
    public async Task Cached_dataset_cannot_hide_changed_endpoint_configuration()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var endpoint = new StudioSourceEndpoint { ReadOnlyVerified = true, PublicQueriesAllowed = true, BaseAddress = "https://source.invalid" };
        fixture.Profile.SourceRegistry["registered"] = endpoint;
        var source = new StudioSource(DateTimeOffset.UtcNow) { NId = "cache-source", TenantNId = fixture.Principal.TenantNId, Name = "cache", EndpointKey = "registered", EndpointConfigurationHash = fixture.SourceExecutor.ConfigurationHash(endpoint), Provider = "RegisteredApi", IsEnabled = true };
        await fixture.Store.InsertSourceAsync(source, CancellationToken.None);
        var document = DatasetBindingTests.Document(source.NId, source.ConfigurationVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)); document["cacheSeconds"] = 60;
        var execution = new DatasetExecution(fixture.Store, fixture.Publishing, fixture.Authorization, fixture.SourceExecutor, new(), TimeProvider.System);
        await execution.ExecuteDocumentAsync(fixture.Principal, "revision-cache", document, new() { ["lineCode"] = "L01" }, false, null, null, CancellationToken.None);
        endpoint.BaseAddress = "https://changed.invalid";
        var refused = await Assert.ThrowsAsync<StudioException>(() => execution.ExecuteDocumentAsync(fixture.Principal, "revision-cache", document, new() { ["lineCode"] = "L01" }, false, null, null, CancellationToken.None));
        Assert.Equal("SOURCE_CHANGED", refused.Code); Assert.Equal(1, fixture.SourceExecutor.Calls);
    }

    [Fact]
    public async Task Inspect_absent_sqlite_is_readonly()
    {
        var folder = Path.Combine(Path.GetTempPath(), "studio-inspect-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = Path.Combine(folder, "absent.db"); var profile = StudioInitializationTests.Profile(file);
            using var database = new StudioDatabase(profile, new ConfigurationBuilder().Build());
            Assert.False((await new StudioServiceInitializer(database, profile, TimeProvider.System).InspectLocalAsync(CancellationToken.None)).Ready);
            Assert.False(File.Exists(file));
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData("DROP INDEX ix_st_resource_list", "STU-001-Resources")]
    [InlineData("DROP INDEX ix_system_audit_fact_query", "STA-001-AuditCore")]
    [InlineData("DROP TRIGGER st_revision_no_update", "STU-001-Resources")]
    public async Task Physical_drift_prevents_checksum_backfill(string change, string migration)
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var db = fixture.Database; var initializer = new StudioServiceInitializer(db, fixture.Profile, TimeProvider.System);
        await db.Client.Ado.ExecuteCommandAsync(change);
        await db.Client.Ado.ExecuteCommandAsync("UPDATE studio_schema_migrations SET checksum=NULL WHERE migration_id=@migration", new { migration });
        Assert.False((await initializer.InspectLocalAsync(CancellationToken.None)).Ready);
        await Assert.ThrowsAsync<StudioException>(() => initializer.InitializeLocalAsync(CancellationToken.None));
        Assert.Equal(1, await db.Client.Ado.GetIntAsync("SELECT COUNT(*) FROM studio_schema_migrations WHERE migration_id=@migration AND checksum IS NULL", new { migration }));
    }

    [Fact]
    public async Task Future_migration_is_rejected_and_database_blocks_revision_mutation()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var created = await fixture.Publishing.CreateAsync(fixture.Principal, new("immutable", "LedgerModel", "不可变", null, null, StudioPublishingTests.Model()), "create", CancellationToken.None);
        var plan = await fixture.Publishing.ValidateAsync(fixture.Principal, created.NId, StudioPublishingTests.Model(), CancellationToken.None);
        var revision = await fixture.Publishing.PublishAsync(fixture.Principal, created.NId, new(created.OptimisticVersion, created.ConcurrencyVersion, "v1", true, plan.DependencyPlanHash), "publish", CancellationToken.None);
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Database.Client.Ado.ExecuteCommandAsync("UPDATE studio_revision SET release_note='tampered' WHERE n_id=@nId", new { nId = revision.NId }));
        await fixture.Database.Client.Ado.ExecuteCommandAsync("INSERT INTO studio_schema_migrations (migration_id,description,checksum,applied_on) VALUES ('STU-999-Future','future','future','2026-10-22T00:00:00Z')");
        var initializer = new StudioServiceInitializer(fixture.Database, fixture.Profile, TimeProvider.System);
        Assert.False((await initializer.InspectLocalAsync(CancellationToken.None)).Ready);
        Assert.Equal("STUDIO_SCHEMA_DRIFT", (await Assert.ThrowsAsync<StudioException>(() => initializer.InitializeLocalAsync(CancellationToken.None))).Code);
    }

    [Fact]
    public async Task Sqlite_preview_samples_100_rows_but_runtime_rejects_oversize_results()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var file = Path.Combine(Path.GetTempPath(), "studio-sample-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var raw = new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ConnectionString;
            await using (var writer = new SqliteConnection(raw))
            {
                await writer.OpenAsync(); await using var command = writer.CreateCommand();
                command.CommandText = "CREATE TABLE work_orders(n_id TEXT); WITH RECURSIVE series(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM series WHERE n<150) INSERT INTO work_orders SELECT printf('%03d',n) FROM series";
                await command.ExecuteNonQueryAsync();
            }
            var endpoint = new StudioSourceEndpoint { Provider = "Sqlite", CatalogKey = "sample", ConnectionStringRef = "source", ReadOnlyVerified = true, PublicQueriesAllowed = true, AllowedObjects = ["work_orders"] };
            fixture.Profile.SourceRegistry["sample"] = endpoint;
            using var http = new HttpClient(); using var executor = new RegisteredSourceExecutor(fixture.Profile, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["source"] = raw }).Build(), http, TimeProvider.System);
            var source = new StudioSource { NId = "sample", Provider = "Sqlite", EndpointKey = "sample", EndpointConfigurationHash = executor.ConfigurationHash(endpoint), AllowedObjectsJson = "[{\"qualifiedName\":\"work_orders\",\"kind\":\"View\"}]", MaxRows = 100, MaxBytes = 1048576, TimeoutSeconds = 30 };
            var document = DatasetBindingTests.Document("sample", "1"); document["rowPolicyKey"] = "public"; document["query"]!["statement"] = "SELECT n_id FROM work_orders ORDER BY n_id";
            var preview = await executor.ExecuteAsync(fixture.Principal, source, document, new(), true, CancellationToken.None);
            Assert.Equal(100, preview.Data!.AsArray().Count); Assert.Equal(100L, preview.RowCount);
            Assert.Equal("STUDIO_LIMIT", (await Assert.ThrowsAsync<StudioException>(() => executor.ExecuteAsync(fixture.Principal, source, document, new(), false, CancellationToken.None))).Code);
            foreach (var (type, value) in new[] { ("Int64", "9007199254740993"), ("Decimal", "12345678901234567890.123456") })
            {
                document["parameters"] = new JsonArray(new JsonObject { ["name"] = "callerValue", ["type"] = type });
                document["query"] = new JsonObject { ["kind"] = "Sql", ["statement"] = "SELECT @mappedValue AS n_id", ["parameters"] = new JsonArray(new JsonObject { ["target"] = "mappedValue", ["source"] = new JsonObject { ["from"] = "PageParam", ["path"] = new JsonArray("callerValue"), ["nullPolicy"] = "Error" } }) };
                var exact = await executor.ExecuteAsync(fixture.Principal, source, document, new() { ["mappedValue"] = value }, false, CancellationToken.None);
                Assert.Equal(value, exact.Data![0]!["n_id"]!.GetValue<string>());
            }
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(file)) File.Delete(file); }
    }
}
