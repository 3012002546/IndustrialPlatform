using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Collaboration.Infrastructure;
using IndustrialPlatform.Collaboration.Infrastructure.Persistence;
using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Passwords;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Seeds;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Infrastructure.Initialization;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Initialization;
using IndustrialPlatform.SystemData.Application.Reliability;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Initialization;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.SystemData;
using IndustrialPlatform.SystemData.Infrastructure.Reliability;
using IndustrialPlatform.UnifiedHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using SqlSugar;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Xunit.Sdk;

namespace IndustrialPlatform.IntegrationTests.UnifiedHost;

[Trait("Category", "Integration")]
public sealed class UnifiedHostLegacyPostgreSqlStartupChainTests
{
    private const string Gate = "PF05_STARTUP_CHAIN_PG";
    private static readonly string[] MatrixServices = ["identity", "systemdata", "referencedata", "collaboration"];

#if PF05_GATE_ENABLED
    [Fact]
#else
    [Fact(Skip = "真实 PostgreSQL 验收需要使用 PF05_GATE_ENABLED=1 构建并设置 PF05_STARTUP_CHAIN_PG=1。")]
#endif
    public async Task Shared_postgresql_legacy_ledgers_are_inspected_read_only_then_upgraded_idempotently()
    {
        RequireGate();

        var adminConnectionString = ReadConnectionString("PF05_STARTUP_CHAIN_PG_ADMIN_DATABASE");
        var temporaryDatabase = $"pf05_legacy_{Guid.NewGuid():N}";
        var temporaryConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = temporaryDatabase,
        }.ConnectionString;

        try
        {
            await CreateDatabaseAsync(adminConnectionString, temporaryDatabase);
            await CreateLegacyFixtureAsync(temporaryConnectionString, temporaryDatabase);

            var beforeInspect = await ReadLegacySnapshotAsync(temporaryConnectionString);

            using (var dbContext = CreateDbContext(temporaryConnectionString))
            {
                var initializers = CreateInitializers(dbContext);
                var invoker = new InProcessServiceInitializationInvoker(initializers);
                var serviceContexts = CreateServiceContexts(temporaryDatabase, "legacy-inspect");
                var inspections = new Dictionary<string, ServiceInitializationState>(StringComparer.OrdinalIgnoreCase);
                foreach (var serviceContext in serviceContexts)
                    inspections[serviceContext.ServiceKey] = await invoker.InspectAsync(serviceContext, CancellationToken.None);

                Assert.Equal(4, inspections.Count);
                Assert.All(inspections, item => Assert.False(item.Value.Ready, item.Key));
                Assert.All(inspections, item => Assert.False(string.IsNullOrWhiteSpace(item.Value.Reason), item.Key));
                foreach (var inspection in inspections)
                {
                    var plan = await invoker.PlanAsync(
                        serviceContexts.Single(context => string.Equals(context.ServiceKey, inspection.Key, StringComparison.OrdinalIgnoreCase)),
                        inspection.Value,
                        CancellationToken.None);
                    Assert.True(plan.RequiresApply, inspection.Key);
                }

                var afterInspect = await ReadLegacySnapshotAsync(temporaryConnectionString);
                Assert.Equal(beforeInspect, afterInspect);

                var firstRecordingInvoker = new RecordingInitializationInvoker(invoker);
                await ModuleMigrationCoordinatorHostedService.RunInitializersAsync(
                    firstRecordingInvoker,
                    initializers,
                    CreateContext(temporaryDatabase, "legacy-startup-1"),
                    CancellationToken.None);
                Assert.Equal(4, firstRecordingInvoker.ApplyCount);

                foreach (var serviceContext in serviceContexts)
                    Assert.True((await invoker.VerifyAsync(serviceContext, CancellationToken.None)).Ready, serviceContext.ServiceKey);
            }

            var afterApply = await ReadLegacySnapshotAsync(temporaryConnectionString);
            Assert.Equal(beforeInspect.IdentityLedger, afterApply.IdentityLedger);
            Assert.Equal(beforeInspect.IdentityUserCount, afterApply.IdentityUserCount);
            Assert.Equal(beforeInspect.SystemDataLedger, afterApply.SystemDataLedger);
            Assert.Equal(beforeInspect.EnvironmentPolicyCount, afterApply.EnvironmentPolicyCount);
            Assert.Equal(beforeInspect.CollaborationLedger, afterApply.CollaborationLedger);
            Assert.Equal(beforeInspect.ConversationCount, afterApply.ConversationCount);
            Assert.Equal(beforeInspect.ReferenceBaselineAppliedOn, afterApply.ReferenceBaselineAppliedOn);
            Assert.Equal(beforeInspect.ReferenceBaselineSeedAppliedOn, afterApply.ReferenceBaselineSeedAppliedOn);
            Assert.Equal(beforeInspect.ReferenceUnitSeedAppliedOn, afterApply.ReferenceUnitSeedAppliedOn);
            Assert.Equal(beforeInspect.ReferenceDictionaryCount, afterApply.ReferenceDictionaryCount);
            Assert.Equal(beforeInspect.ReferenceDimensionCount, afterApply.ReferenceDimensionCount);
            Assert.Equal(beforeInspect.ReferenceUnitCount, afterApply.ReferenceUnitCount);

            await using (var connection = new NpgsqlConnection(temporaryConnectionString))
            {
                await connection.OpenAsync();
                Assert.Equal(1, await CountAsync(connection, "identity_schema_migrations", "checksum"));
                Assert.Equal(1, await CountAsync(connection, "system_data_schema_migrations", "checksum"));
                Assert.Equal(1, await CountAsync(connection, "collaboration_schema_migrations", "checksum"));
                Assert.Equal(1, await CountAsync(connection, "reference_data.schema_migrations", "checksum"));
                Assert.Equal(1, await CountAsync(connection, "reference_data.seed_ledger", "checksum"));
                Assert.False(await IsNullAsync(connection, "identity_schema_migrations", "ID-004-01", "checksum"));
                Assert.False(await IsNullAsync(connection, "system_data_schema_migrations", "SDM-001-01", "checksum"));
                Assert.False(await IsNullAsync(connection, "collaboration_schema_migrations", "PF05-001", "checksum"));
            }

            using var repeatDbContext = CreateDbContext(temporaryConnectionString);
            var repeatInitializers = CreateInitializers(repeatDbContext);
            var repeatInvoker = new RecordingInitializationInvoker(new InProcessServiceInitializationInvoker(repeatInitializers));
            await ModuleMigrationCoordinatorHostedService.RunInitializersAsync(
                repeatInvoker,
                repeatInitializers,
                CreateContext(temporaryDatabase, "legacy-startup-2"),
                CancellationToken.None);
            Assert.Equal(0, repeatInvoker.ApplyCount);

            var afterRepeat = await ReadLegacySnapshotAsync(temporaryConnectionString);
            Assert.Equal(afterApply, afterRepeat);
        }
        catch (Exception exception) when (exception is not XunitException)
        {
            throw new Xunit.Sdk.XunitException($"隔离 PostgreSQL 启动链路失败:{exception.GetType().Name}:{exception.Message}");
        }
        finally
        {
            await DropDatabaseAsync(adminConnectionString, temporaryDatabase);
        }
    }

#if PF05_GATE_ENABLED
    [Fact]
#else
    [Fact(Skip = "真实 PostgreSQL 验收需要使用 PF05_GATE_ENABLED=1 构建并设置 PF05_STARTUP_CHAIN_PG=1。")]
#endif
    public async Task PostgreSql_drift_and_invalid_ledger_facts_fail_closed_before_next_migration()
    {
        RequireGate();

        var adminConnectionString = ReadConnectionString("PF05_STARTUP_CHAIN_PG_ADMIN_DATABASE");
        var temporaryDatabase = $"pf05_drift_{Guid.NewGuid():N}";
        var temporaryConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = temporaryDatabase,
        }.ConnectionString;
        try
        {
            await CreateDatabaseAsync(adminConnectionString, temporaryDatabase);
            await AssertIdentityDriftCaseAsync(temporaryConnectionString, temporaryDatabase, "checksum", wrongDescription: false, physicalDrift: false);
            await AssertIdentityDriftCaseAsync(temporaryConnectionString, temporaryDatabase, "description", wrongDescription: true, physicalDrift: false);
            await AssertIdentityDriftCaseAsync(temporaryConnectionString, temporaryDatabase, "physical", wrongDescription: false, physicalDrift: true);
            await AssertSystemDataPhysicalDriftCaseAsync(temporaryConnectionString, temporaryDatabase);
            await AssertCollaborationPhysicalDriftCaseAsync(temporaryConnectionString, temporaryDatabase);
            await AssertReferenceDataPhysicalDriftCaseAsync(temporaryConnectionString, temporaryDatabase);
        }
        catch (Exception exception) when (exception is not XunitException)
        {
            throw new XunitException($"隔离 PostgreSQL 漂移门禁失败:{exception.GetType().Name}:{exception.Message}");
        }
        finally
        {
            await DropDatabaseAsync(adminConnectionString, temporaryDatabase);
        }
    }

#if PF05_GATE_ENABLED
    [Fact]
#else
    [Fact(Skip = "真实 PostgreSQL 验收需要使用 PF05_GATE_ENABLED=1 构建并设置 PF05_STARTUP_CHAIN_PG=1。")]
#endif
    public async Task PostgreSql_shared_per_service_and_search_path_matrix_isolated()
    {
        RequireGate();

        var adminConnectionString = ReadConnectionString("PF05_STARTUP_CHAIN_PG_ADMIN_DATABASE");
        var temporaryDatabase = $"pf05_matrix_{Guid.NewGuid():N}";
        var temporaryConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = temporaryDatabase,
        }.ConnectionString;
        try
        {
            await CreateDatabaseAsync(adminConnectionString, temporaryDatabase);
            var shared = new DatabaseTopology(
                "Development",
                DatabaseTopologyMode.Shared,
                temporaryDatabase,
                null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            var sharedTargets = MatrixServices
                .Select(service => DatabaseTopologyResolver.Resolve(shared, service, DatabaseProvider.PostgreSQL, $"{service}_db"))
                .ToArray();
            Assert.All(sharedTargets, target => Assert.True(target.IsSharedPhysicalDatabase));
            Assert.All(sharedTargets, target => Assert.Equal(temporaryDatabase, target.PhysicalDatabaseName));

            var perService = new DatabaseTopology(
                "Test",
                DatabaseTopologyMode.PerService,
                null,
                null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["identity"] = $"{temporaryDatabase}_identity",
                    ["systemdata"] = $"{temporaryDatabase}_systemdata",
                    ["referencedata"] = $"{temporaryDatabase}_referencedata",
                    ["collaboration"] = $"{temporaryDatabase}_collaboration",
                });
            var perServiceTargets = MatrixServices
                .Select(service => DatabaseTopologyResolver.Resolve(perService, service, DatabaseProvider.PostgreSQL, $"{service}_db"))
                .ToArray();
            Assert.All(perServiceTargets, target => Assert.False(target.IsSharedPhysicalDatabase));
            Assert.Equal(4, perServiceTargets.Select(target => target.PhysicalDatabaseName).Distinct(StringComparer.Ordinal).Count());
            Assert.ThrowsAny<Exception>(() => DatabaseTopologyResolver.Resolve(
                perService with { ServiceDatabases = new Dictionary<string, string>() },
                "identity",
                DatabaseProvider.PostgreSQL,
                "identity_db"));

            await CreateSearchPathProbeAsync(temporaryConnectionString);
            await using var first = new NpgsqlConnection(ConnectionForSchema(temporaryConnectionString, "pf05_path_a"));
            await using var second = new NpgsqlConnection(ConnectionForSchema(temporaryConnectionString, "pf05_path_b"));
            await first.OpenAsync();
            await second.OpenAsync();
            Assert.Equal("pf05_path_a", await ScalarAsync(first, "SELECT current_schema()"));
            Assert.Equal("path-a", await ScalarAsync(first, "SELECT probe_value FROM search_path_probe"));
            Assert.Equal("pf05_path_b", await ScalarAsync(second, "SELECT current_schema()"));
            Assert.Equal("path-b", await ScalarAsync(second, "SELECT probe_value FROM search_path_probe"));
        }
        catch (Exception exception) when (exception is not XunitException)
        {
            throw new XunitException($"隔离 PostgreSQL 拓扑/search_path 矩阵失败:{exception.GetType().Name}:{exception.Message}");
        }
        finally
        {
            await DropDatabaseAsync(adminConnectionString, temporaryDatabase);
        }
    }

#if PF05_GATE_ENABLED
    [Fact]
#else
    [Fact(Skip = "真实 PostgreSQL 验收需要使用 PF05_GATE_ENABLED=1 构建并设置 PF05_STARTUP_CHAIN_PG=1。")]
#endif
    public async Task PostgreSql_per_service_four_physical_databases_run_owned_initialization_chains()
    {
        RequireGate();

        var adminConnectionString = ReadConnectionString("PF05_STARTUP_CHAIN_PG_ADMIN_DATABASE");
        var suffix = Guid.NewGuid().ToString("N");
        var serviceDatabases = MatrixServices.ToDictionary(
            service => service,
            service => $"pf05ps_{suffix}_{service}",
            StringComparer.OrdinalIgnoreCase);
        var topology = new DatabaseTopology(
            "Test",
            DatabaseTopologyMode.PerService,
            null,
            null,
            serviceDatabases);

        var createdDatabases = new List<string>();
        Exception? primaryFailure = null;
        try
        {
            foreach (var database in serviceDatabases.Values)
            {
                await CreateDatabaseAsync(adminConnectionString, database);
                createdDatabases.Add(database);
            }

            var identityConnectionString = CreatePhysicalConnectionString(
                adminConnectionString,
                serviceDatabases["identity"]);

            foreach (var service in MatrixServices)
            {
                var target = DatabaseTopologyResolver.Resolve(
                    topology,
                    service,
                    DatabaseProvider.PostgreSQL,
                    $"{service}_db");
                Assert.False(target.IsSharedPhysicalDatabase, service);
                Assert.Equal(serviceDatabases[service], target.PhysicalDatabaseName);

                var connectionString = CreatePhysicalConnectionString(adminConnectionString, target.PhysicalDatabaseName);
                await CreatePerServiceLegacyFixtureAsync(service, connectionString, target.PhysicalDatabaseName);
            }

            foreach (var service in MatrixServices)
            {
                var target = DatabaseTopologyResolver.Resolve(
                    topology,
                    service,
                    DatabaseProvider.PostgreSQL,
                    $"{service}_db");
                var connectionString = CreatePhysicalConnectionString(adminConnectionString, target.PhysicalDatabaseName);
                var beforeInspect = await ReadPerServiceSnapshotAsync(connectionString, service);

                using (var dbContext = CreateDbContext(connectionString))
                {
                    var initializer = CreatePerServiceInitializer(service, dbContext, identityConnectionString);
                    var context = CreateServiceContext(
                        target,
                        $"per-service-{service}-startup-1",
                        service,
                        DesiredVersionFor(service));
                    var inspected = await initializer.InspectAsync(context, CancellationToken.None);
                    var plan = await initializer.PlanAsync(context, inspected, CancellationToken.None);

                    Assert.False(inspected.Ready, service);
                    Assert.False(string.IsNullOrWhiteSpace(inspected.Reason), service);
                    Assert.True(plan.RequiresApply, service);

                    var afterInspect = await ReadPerServiceSnapshotAsync(connectionString, service);
                    Assert.Equal(beforeInspect, afterInspect);

                    var executionContext = context with { DesiredVersion = plan.DesiredVersion };
                    var applied = await initializer.ApplyAsync(executionContext, plan, CancellationToken.None);
                    Assert.True(applied.Ready, $"{service}:{applied.Reason}");
                    var verified = await initializer.VerifyAsync(executionContext, CancellationToken.None);
                    Assert.True(verified.Ready, $"{service}:{verified.Reason}");
                }

                var afterApply = await ReadPerServiceSnapshotAsync(connectionString, service);
                AssertPerServiceSnapshotPreserved(beforeInspect, afterApply);
                await AssertPerServiceMetadataAsync(connectionString, service);

                using var repeatDbContext = CreateDbContext(connectionString);
                var repeatInitializer = CreatePerServiceInitializer(service, repeatDbContext, identityConnectionString);
                var repeatContext = CreateServiceContext(
                    target,
                    $"per-service-{service}-startup-2",
                    service,
                    DesiredVersionFor(service));
                var repeatInvoker = new RecordingInitializationInvoker(
                    new InProcessServiceInitializationInvoker([repeatInitializer]));
                var repeatedInspection = await repeatInvoker.InspectAsync(repeatContext, CancellationToken.None);
                var repeatedPlan = await repeatInvoker.PlanAsync(repeatContext, repeatedInspection, CancellationToken.None);
                var repeatedVerification = await repeatInvoker.VerifyAsync(repeatContext, CancellationToken.None);

                Assert.True(repeatedInspection.Ready, $"{service}:{repeatedInspection.Reason}");
                Assert.False(repeatedPlan.RequiresApply, service);
                Assert.Equal(0, repeatInvoker.ApplyCount);
                Assert.True(repeatedVerification.Ready, $"{service}:{repeatedVerification.Reason}");
                Assert.Equal(afterApply, await ReadPerServiceSnapshotAsync(connectionString, service));
            }

            foreach (var service in MatrixServices)
            {
                var connectionString = CreatePhysicalConnectionString(adminConnectionString, serviceDatabases[service]);
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();

                foreach (var ledger in new[]
                {
                    (Service: "identity", Table: "identity_schema_migrations", Schema: "public"),
                    (Service: "systemdata", Table: "system_data_schema_migrations", Schema: "public"),
                    (Service: "referencedata", Table: "schema_migrations", Schema: "reference_data"),
                    (Service: "collaboration", Table: "collaboration_schema_migrations", Schema: "public"),
                })
                {
                    var exists = await TableExistsAsync(connection, ledger.Schema, ledger.Table);
                    Assert.Equal(string.Equals(service, ledger.Service, StringComparison.OrdinalIgnoreCase), exists);
                }
            }
        }
        catch (Exception exception)
        {
            primaryFailure = exception is XunitException
                ? exception
                : new XunitException($"隔离 PostgreSQL PerService 四库启动链路失败:{exception.GetType().Name}:{exception.Message}");
        }

        var cleanupFailures = await DropDatabasesBestEffortAsync(adminConnectionString, createdDatabases);
        if (primaryFailure is not null)
        {
            if (cleanupFailures.Count > 0)
                throw new XunitException($"{primaryFailure.Message};临时库清理失败:{string.Join('|', cleanupFailures)}");
            throw primaryFailure;
        }

        if (cleanupFailures.Count > 0)
            throw new XunitException($"PerService 临时库清理失败:{string.Join('|', cleanupFailures)}");
    }

    private static void RequireGate()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(Gate), "1", StringComparison.Ordinal))
            throw SkipException.ForSkip($"真实 PostgreSQL 验收需要设置 {Gate}=1。");
    }

    private static SqlSugarDbContext CreateDbContext(string connectionString) =>
        new(Options.Create(new SqlSugarOptions
        {
            ConnectionString = connectionString,
            DbType = DbType.PostgreSQL,
        }));

    private static IReadOnlyList<ServiceInitializationContext> CreateServiceContexts(string database, string operation) =>
        [
            CreateServiceContext(database, "identity", operation),
            CreateServiceContext(database, "systemdata", operation),
            CreateServiceContext(database, "referencedata", operation),
            CreateServiceContext(database, "collaboration", operation),
        ];

    private static ServiceInitializationContext CreateServiceContext(
        string database,
        string serviceKey,
        string operation,
        string desiredVersion = "",
        DatabaseTopologyMode mode = DatabaseTopologyMode.Shared) =>
        new(
            mode == DatabaseTopologyMode.Shared ? "Development" : "Test",
            "tenant-pf05-legacy",
            operation,
            serviceKey,
            serviceKey,
            new ResolvedDatabaseTarget(
                mode == DatabaseTopologyMode.Shared ? "Development" : "Test",
                mode,
                serviceKey,
                DatabaseProvider.PostgreSQL,
                $"{serviceKey}_db",
                database,
                mode == DatabaseTopologyMode.Shared),
            desiredVersion,
            ServiceInitializationPolicy.Standard,
            $"trace-pf05-{serviceKey}");

    private static ServiceInitializationContext CreateServiceContext(
        ResolvedDatabaseTarget target,
        string operation,
        string serviceKey,
        string desiredVersion = "") =>
        new(
            target.EnvironmentName,
            "tenant-pf05-per-service",
            operation,
            serviceKey,
            serviceKey,
            target,
            desiredVersion,
            ServiceInitializationPolicy.Standard,
            $"trace-pf05-{serviceKey}");

    private static string DesiredVersionFor(string service) =>
        string.Equals(service, "referencedata", StringComparison.OrdinalIgnoreCase)
            ? ReferenceDataServiceInitializer.CurrentVersion
            : string.Empty;

    private static async Task CreateLegacyFixtureAsync(string connectionString, string database)
    {
        using (var dbContext = CreateDbContext(connectionString))
        {
            foreach (var step in IdentitySchemaMigrations.All)
                await step.Apply(dbContext.SqlSugar, CancellationToken.None);
            await SystemDataSchemaMigrations.All[0].Apply(dbContext.SqlSugar, CancellationToken.None);
            await CollaborationSchemaMigrations.All[0].Apply(dbContext.SqlSugar, CancellationToken.None);
        }

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE identity_schema_migrations (
                    migration_id TEXT PRIMARY KEY NOT NULL,
                    description TEXT NOT NULL,
                    applied_on TIMESTAMPTZ NOT NULL
                );
                CREATE TABLE system_data_schema_migrations (
                    migration_id TEXT PRIMARY KEY NOT NULL,
                    description TEXT NOT NULL,
                    applied_on TIMESTAMPTZ NOT NULL
                );
                CREATE TABLE collaboration_schema_migrations (
                    migration_id TEXT PRIMARY KEY NOT NULL,
                    description TEXT NOT NULL,
                    checksum TEXT NULL,
                    applied_on TIMESTAMPTZ NOT NULL
                );
                INSERT INTO identity_schema_migrations (migration_id,description,applied_on)
                VALUES ('ID-004-01','create identity_user','2024-01-02T03:04:05+00:00');
                INSERT INTO system_data_schema_migrations (migration_id,description,applied_on)
                VALUES ('SDM-001-01','create system_data_database_environment_policy','2024-01-02T03:04:05+00:00');
                INSERT INTO collaboration_schema_migrations (migration_id,description,checksum,applied_on)
                VALUES ('PF05-001','create collaboration messaging tables',NULL,'2024-01-02T03:04:05+00:00');

                INSERT INTO identity_user
                    (id,is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version,
                     tenant_n_id,n_id,normalized_n_id,login_name,normalized_login_name,name,password_hash,email,phone,status,
                     failed_login_count,locked_until,auth_version,last_login_on,must_change_password)
                VALUES
                    ('10000000-0000-0000-0000-000000000001',false,false,false,'Legacy.IdentityUser',
                     '2024-01-02T03:04:05+00:00','2024-01-02T03:04:05+00:00',1,'10000000-0000-0000-0000-000000000002',
                     'tenant-pf05-legacy','legacy-user','LEGACY-USER','legacy.user','LEGACY.USER','Legacy User','legacy-hash',NULL,NULL,1,
                     0,NULL,1,NULL,false);

                INSERT INTO system_data_database_environment_policy
                    (id,is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version,
                     tenant_n_id,environment_n_id,environment_kind,approval_required,backup_required,plan_ttl_seconds,
                     plan_timeout_seconds,apply_timeout_seconds,max_pre_migration_retries,policy_revision)
                VALUES
                    ('20000000-0000-0000-0000-000000000001',false,false,false,'Legacy.EnvironmentPolicy',
                     '2024-01-02T03:04:05+00:00','2024-01-02T03:04:05+00:00',1,'20000000-0000-0000-0000-000000000002',
                     'tenant-pf05-legacy','Development',1,false,false,3600,3600,3600,3,1);

                INSERT INTO collaboration_conversation
                    (id,is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version,
                     tenant_n_id,n_id,participant_low_user_n_id,participant_high_user_n_id,status,last_message_sequence,
                     last_message_n_id,last_message_on,retention_floor_sequence)
                VALUES
                    ('30000000-0000-0000-0000-000000000001',false,false,false,'Legacy.Conversation',
                     '2024-01-02T03:04:05+00:00','2024-01-02T03:04:05+00:00',1,'30000000-0000-0000-0000-000000000002',
                     'tenant-pf05-legacy','legacy-conversation','user-a','user-b','Active',0,NULL,NULL,0);
                """;
            await command.ExecuteNonQueryAsync();
        }

        await CreateCurrentReferenceDataSchemaAsync(connectionString, database);
        await DowngradeReferenceDataFixtureAsync(connectionString);
    }

    private static async Task CreatePerServiceLegacyFixtureAsync(
        string service,
        string connectionString,
        string database)
    {
        switch (service)
        {
            case "identity":
                await CreateIdentityLegacyFixtureAsync(connectionString);
                break;
            case "systemdata":
                await CreateSystemDataLegacyFixtureAsync(connectionString);
                break;
            case "referencedata":
                await CreateCurrentReferenceDataSchemaAsync(connectionString, database);
                await DowngradeReferenceDataFixtureAsync(connectionString);
                break;
            case "collaboration":
                await CreateCollaborationLegacyFixtureAsync(connectionString);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(service), service, "未知 PerService 夹具服务。");
        }
    }

    private static async Task CreateIdentityLegacyFixtureAsync(string connectionString)
    {
        using (var dbContext = CreateDbContext(connectionString))
        {
            foreach (var step in IdentitySchemaMigrations.All)
                await step.Apply(dbContext.SqlSugar, CancellationToken.None);
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE identity_schema_migrations (
                migration_id TEXT PRIMARY KEY NOT NULL,
                description TEXT NOT NULL,
                applied_on TIMESTAMPTZ NOT NULL
            );
            INSERT INTO identity_schema_migrations (migration_id,description,applied_on)
            VALUES ('ID-004-01','create identity_user','2024-01-02T03:04:05+00:00');
            INSERT INTO identity_user
                (id,is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version,
                 tenant_n_id,n_id,normalized_n_id,login_name,normalized_login_name,name,password_hash,email,phone,status,
                 failed_login_count,locked_until,auth_version,last_login_on,must_change_password)
            VALUES
                ('10000000-0000-0000-0000-000000000001',false,false,false,'Legacy.IdentityUser',
                 '2024-01-02T03:04:05+00:00','2024-01-02T03:04:05+00:00',1,'10000000-0000-0000-0000-000000000002',
                 'tenant-pf05-per-service','legacy-user','LEGACY-USER','legacy.user','LEGACY.USER','Legacy User','legacy-hash',NULL,NULL,1,
                 0,NULL,1,NULL,false);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateSystemDataLegacyFixtureAsync(string connectionString)
    {
        using (var dbContext = CreateDbContext(connectionString))
            await SystemDataSchemaMigrations.All[0].Apply(dbContext.SqlSugar, CancellationToken.None);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE system_data_schema_migrations (
                migration_id TEXT PRIMARY KEY NOT NULL,
                description TEXT NOT NULL,
                applied_on TIMESTAMPTZ NOT NULL
            );
            INSERT INTO system_data_schema_migrations (migration_id,description,applied_on)
            VALUES ('SDM-001-01','create system_data_database_environment_policy','2024-01-02T03:04:05+00:00');
            INSERT INTO system_data_database_environment_policy
                (id,is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version,
                 tenant_n_id,environment_n_id,environment_kind,approval_required,backup_required,plan_ttl_seconds,
                 plan_timeout_seconds,apply_timeout_seconds,max_pre_migration_retries,policy_revision)
            VALUES
                ('20000000-0000-0000-0000-000000000001',false,false,false,'Legacy.EnvironmentPolicy',
                 '2024-01-02T03:04:05+00:00','2024-01-02T03:04:05+00:00',1,'20000000-0000-0000-0000-000000000002',
                 'tenant-pf05-per-service','Development',1,false,false,3600,3600,3600,3,1);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateCollaborationLegacyFixtureAsync(string connectionString)
    {
        using (var dbContext = CreateDbContext(connectionString))
            await CollaborationSchemaMigrations.All[0].Apply(dbContext.SqlSugar, CancellationToken.None);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE collaboration_schema_migrations (
                migration_id TEXT PRIMARY KEY NOT NULL,
                description TEXT NOT NULL,
                checksum TEXT NULL,
                applied_on TIMESTAMPTZ NOT NULL
            );
            INSERT INTO collaboration_schema_migrations (migration_id,description,checksum,applied_on)
            VALUES ('PF05-001','create collaboration messaging tables',NULL,'2024-01-02T03:04:05+00:00');
            INSERT INTO collaboration_conversation
                (id,is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version,
                 tenant_n_id,n_id,participant_low_user_n_id,participant_high_user_n_id,status,last_message_sequence,
                 last_message_n_id,last_message_on,retention_floor_sequence)
            VALUES
                ('30000000-0000-0000-0000-000000000001',false,false,false,'Legacy.Conversation',
                 '2024-01-02T03:04:05+00:00','2024-01-02T03:04:05+00:00',1,'30000000-0000-0000-0000-000000000002',
                 'tenant-pf05-per-service','legacy-conversation','user-a','user-b','Active',0,NULL,NULL,0);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateCurrentReferenceDataSchemaAsync(string connectionString, string database)
    {
        using var dbContext = CreateDbContext(connectionString);
        var initializer = new ReferenceDataServiceInitializer(new ReferenceDataInitializationLedger(dbContext));
        var context = CreateServiceContext(
            database,
            "referencedata",
            "reference-fixture",
            ReferenceDataServiceInitializer.CurrentVersion);
        var inspection = await initializer.InspectAsync(context, CancellationToken.None);
        var plan = await initializer.PlanAsync(context, inspection, CancellationToken.None);
        var applied = await initializer.ApplyAsync(context, plan, CancellationToken.None);
        Assert.True(applied.Ready, applied.Reason);
    }

    private static async Task DowngradeReferenceDataFixtureAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            ALTER TABLE reference_data.seed_ledger ALTER COLUMN checksum DROP NOT NULL;
            UPDATE reference_data.schema_migrations
               SET checksum=NULL,target_identity=NULL,applied_on='2024-01-02T03:04:05+00:00'
             WHERE migration_id='{ReferenceDataServiceInitializer.BaselineVersion}';
            UPDATE reference_data.seed_ledger
               SET checksum=NULL,scope=NULL,applied_on='2024-01-02T03:04:05+00:00'
             WHERE seed_key IN ('{ReferenceDataServiceInitializer.BaselineSeedKey}','{IndustrialPlatform.ReferenceData.Infrastructure.UnitOfMeasure.UnitOfMeasureSystemSeed.SeedKey}');
            INSERT INTO reference_data.dictionary_definition
                (id,tenant_nid,scope_type,n_id,name,description,revision,status,published_on,published_by,
                 is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version)
            VALUES
                ('40000000-0000-0000-0000-000000000001',NULL,'Platform','LEGACY_STATUS','Legacy status','legacy description',1,
                 'Published','2024-01-02T03:04:05+00:00','legacy-fixture',false,false,false,'Legacy.Dictionary',
                 '2024-01-02T03:04:05+00:00','2024-01-02T03:04:05+00:00',1,'40000000-0000-0000-0000-000000000002');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static IReadOnlyList<IServiceInitializer> CreateInitializers(SqlSugarDbContext dbContext)
    {
        var identityMigrationRunner = new IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations.SchemaMigrationRunner(
            dbContext,
            IdentitySchemaMigrations.All,
            NullLogger<IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations.SchemaMigrationRunner>.Instance);
        var credentialStore = new BootstrapCredentialStore(dbContext);
        var userRepository = new UserRepository(dbContext);
        var store = new BootstrapStore(dbContext, userRepository);
        var hasher = new BcryptPasswordHasher();
        var seedRunner = new IdentitySeedRunner(dbContext, hasher, credentialStore);
        var identityInitialization = new IdentityInitializationService(identityMigrationRunner, seedRunner, store);
        var identity = new IdentityServiceInitializer(
            identityInitialization,
            new BootstrapService(store, credentialStore, new TemporaryPasswordGenerator(), hasher));

        var systemMigrationRunner = new IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations.SchemaMigrationRunner(
            dbContext,
            SystemDataSchemaMigrations.All,
            NullLogger<IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations.SchemaMigrationRunner>.Instance);
        var controlPlaneStore = new SqlControlPlaneStore(dbContext);
        var managementStore = new IndustrialPlatform.Identity.Infrastructure.Management.ManagementStore(
            dbContext,
            userRepository,
            new RoleRepository(dbContext),
            new PermissionRepository(dbContext));
        var permissionRegistry = new IndustrialPlatform.UnifiedHost.InProcessIdentityPermissionRegistry(managementStore);
        var baselineConfiguration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>()).Build();
        var baselineSeeder = new SystemDataBaselineSeedRunner(
            baselineConfiguration,
            controlPlaneStore,
            NullLogger<SystemDataBaselineSeedRunner>.Instance,
            permissionRegistry);
        var systemData = new SystemDataServiceInitializer(
            systemMigrationRunner,
            dbContext,
            controlPlaneStore,
            baselineSeeder);

        var referenceData = new ReferenceDataServiceInitializer(new ReferenceDataInitializationLedger(dbContext));
        var collaboration = new CollaborationServiceInitializer(dbContext);
        return [identity, systemData, referenceData, collaboration];
    }

    private static IServiceInitializer CreatePerServiceInitializer(
        string service,
        SqlSugarDbContext dbContext,
        string identityConnectionString)
    {
        if (string.Equals(service, "systemdata", StringComparison.OrdinalIgnoreCase))
        {
            var systemMigrationRunner = new IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations.SchemaMigrationRunner(
                dbContext,
                SystemDataSchemaMigrations.All,
                NullLogger<IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations.SchemaMigrationRunner>.Instance);
            var controlPlaneStore = new SqlControlPlaneStore(dbContext);
            var baselineConfiguration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>()).Build();
            var baselineSeeder = new SystemDataBaselineSeedRunner(
                baselineConfiguration,
                controlPlaneStore,
                NullLogger<SystemDataBaselineSeedRunner>.Instance,
                new PostgreSqlIdentityPermissionRegistry(identityConnectionString));
            return new SystemDataServiceInitializer(
                systemMigrationRunner,
                dbContext,
                controlPlaneStore,
                baselineSeeder);
        }

        return CreateInitializers(dbContext).Single(item =>
            string.Equals(item.ServiceKey, service, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task AssertIdentityDriftCaseAsync(
        string baseConnectionString,
        string database,
        string caseName,
        bool wrongDescription,
        bool physicalDrift)
    {
        var schema = $"pf05_identity_{caseName}_{Guid.NewGuid():N}";
        await CreateSchemaAsync(baseConnectionString, schema);
        var connectionString = ConnectionForSchema(baseConnectionString, schema);
        var step = IdentitySchemaMigrations.All[0];
        var checksum = Checksum(step.Id, step.Description);
        var storedChecksum = wrongDescription
            ? "NULL"
            : $"'{(physicalDrift ? checksum : "wrong-checksum")}'";
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                CREATE TABLE identity_schema_migrations (
                    migration_id TEXT PRIMARY KEY NOT NULL,
                    description TEXT NOT NULL,
                    checksum TEXT NULL,
                    applied_on TIMESTAMPTZ NOT NULL
                );
                INSERT INTO identity_schema_migrations (migration_id,description,checksum,applied_on)
                VALUES ('{step.Id}',
                        '{(wrongDescription ? "wrong legacy description" : step.Description)}',
                        {storedChecksum},
                        '2024-01-02T03:04:05+00:00');
                """;
            await command.ExecuteNonQueryAsync();
        }

        using var dbContext = CreateDbContext(connectionString);
        var runner = new IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations.SchemaMigrationRunner(
            dbContext,
            IdentitySchemaMigrations.All,
            NullLogger<IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations.SchemaMigrationRunner>.Instance);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ApplyPendingAsync());
        Assert.Contains(
            wrongDescription ? "description drift" : physicalDrift ? "physical schema drift" : "checksum drift",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        await using var probe = new NpgsqlConnection(connectionString);
        await probe.OpenAsync();
        Assert.Equal(0, await CountRowsAsync(probe, "identity_schema_migrations", "ID-004-02"));
    }

    private static async Task AssertSystemDataPhysicalDriftCaseAsync(string baseConnectionString, string database)
    {
        var schema = $"pf05_systemdata_{Guid.NewGuid():N}";
        await CreateSchemaAsync(baseConnectionString, schema);
        var connectionString = ConnectionForSchema(baseConnectionString, schema);
        var step = SystemDataSchemaMigrations.All[0];
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                CREATE TABLE system_data_schema_migrations (
                    migration_id TEXT PRIMARY KEY NOT NULL,
                    description TEXT NOT NULL,
                    checksum TEXT NULL,
                    applied_on TIMESTAMPTZ NOT NULL
                );
                INSERT INTO system_data_schema_migrations (migration_id,description,checksum,applied_on)
                VALUES ('{step.Id}','{step.Description}','{Checksum(step.Id, step.Description)}','2024-01-02T03:04:05+00:00');
                """;
            await command.ExecuteNonQueryAsync();
        }

        using var dbContext = CreateDbContext(connectionString);
        var runner = new IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations.SchemaMigrationRunner(
            dbContext,
            SystemDataSchemaMigrations.All,
            NullLogger<IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations.SchemaMigrationRunner>.Instance);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ApplyPendingAsync());
        Assert.Contains("physical schema drift", exception.Message, StringComparison.OrdinalIgnoreCase);

        await using var probe = new NpgsqlConnection(connectionString);
        await probe.OpenAsync();
        Assert.Equal(0, await CountRowsAsync(probe, "system_data_schema_migrations", "SDM-001-02"));
    }

    private static async Task AssertCollaborationPhysicalDriftCaseAsync(string baseConnectionString, string database)
    {
        var schema = $"pf05_collaboration_{Guid.NewGuid():N}";
        await CreateSchemaAsync(baseConnectionString, schema);
        var connectionString = ConnectionForSchema(baseConnectionString, schema);
        var step = CollaborationSchemaMigrations.All[0];
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                CREATE TABLE collaboration_schema_migrations (
                    migration_id TEXT PRIMARY KEY NOT NULL,
                    description TEXT NOT NULL,
                    checksum TEXT NULL,
                    applied_on TIMESTAMPTZ NOT NULL
                );
                INSERT INTO collaboration_schema_migrations (migration_id,description,checksum,applied_on)
                VALUES ('{step.Id}','{step.Description}','{Checksum(step.Id, step.Description)}','2024-01-02T03:04:05+00:00');
                """;
            await command.ExecuteNonQueryAsync();
        }

        using var dbContext = CreateDbContext(connectionString);
        var initializer = new CollaborationServiceInitializer(dbContext);
        var context = CreateServiceContext(database, "collaboration", "collaboration-drift");
        var plan = new ServiceInitializationPlan(
            "collaboration",
            "collaboration",
            step.Id,
            CollaborationSchemaMigrations.All[^1].Id,
            true,
            ["collaboration-schema-migration"]);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => initializer.ApplyAsync(context, plan, CancellationToken.None));
        Assert.Contains("physical schema drift", exception.Message, StringComparison.OrdinalIgnoreCase);

        await using var probe = new NpgsqlConnection(connectionString);
        await probe.OpenAsync();
        Assert.Equal(0, await CountRowsAsync(probe, "collaboration_schema_migrations", "PF05-002"));
    }

    private static async Task AssertReferenceDataPhysicalDriftCaseAsync(string connectionString, string database)
    {
        await CreateCurrentReferenceDataSchemaAsync(connectionString, database);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                DELETE FROM reference_data.schema_migrations WHERE migration_id='reference-data-2.7-003';
                DROP TABLE reference_data.dictionary_definition CASCADE;
                """;
            await command.ExecuteNonQueryAsync();
        }

        using var dbContext = CreateDbContext(connectionString);
        var initializer = new ReferenceDataServiceInitializer(new ReferenceDataInitializationLedger(dbContext));
        var context = CreateServiceContext(
            database,
            "referencedata",
            "referencedata-drift",
            ReferenceDataServiceInitializer.CurrentVersion);
        var inspection = await initializer.InspectAsync(context, CancellationToken.None);
        var plan = await initializer.PlanAsync(context, inspection, CancellationToken.None);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => initializer.ApplyAsync(context, plan, CancellationToken.None));
        Assert.Contains("physical schema drift", exception.Message, StringComparison.OrdinalIgnoreCase);

        await using var probe = new NpgsqlConnection(connectionString);
        await probe.OpenAsync();
        Assert.Equal(0, await CountRowsAsync(probe, "reference_data.schema_migrations", "reference-data-2.7-003"));
    }

    private static async Task CreateSearchPathProbeAsync(string connectionString)
    {
        await CreateSchemaAsync(connectionString, "pf05_path_a");
        await CreateSchemaAsync(connectionString, "pf05_path_b");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE pf05_path_a.search_path_probe (probe_value TEXT NOT NULL);
            CREATE TABLE pf05_path_b.search_path_probe (probe_value TEXT NOT NULL);
            INSERT INTO pf05_path_a.search_path_probe VALUES ('path-a');
            INSERT INTO pf05_path_b.search_path_probe VALUES ('path-b');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<LegacySnapshot> ReadLegacySnapshotAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return new LegacySnapshot(
            await RequiredScalarAsync(connection, "SELECT migration_id || '|' || description || '|' || applied_on::text FROM identity_schema_migrations WHERE migration_id='ID-004-01'"),
            await CountRowsAsync(connection, "identity_user"),
            await RequiredScalarAsync(connection, "SELECT migration_id || '|' || description || '|' || applied_on::text FROM system_data_schema_migrations WHERE migration_id='SDM-001-01'"),
            await CountRowsAsync(connection, "system_data_database_environment_policy"),
            await RequiredScalarAsync(connection, "SELECT migration_id || '|' || description || '|' || applied_on::text FROM collaboration_schema_migrations WHERE migration_id='PF05-001'"),
            await CountRowsAsync(connection, "collaboration_conversation"),
            await RequiredScalarAsync(connection, "SELECT applied_on::text FROM reference_data.schema_migrations WHERE migration_id='reference-data-2.7-001'"),
            await RequiredScalarAsync(connection, "SELECT applied_on::text FROM reference_data.seed_ledger WHERE seed_key='reference-data.baseline' AND seed_version='reference-data-2.7-001'"),
            await RequiredScalarAsync(connection, "SELECT applied_on::text FROM reference_data.seed_ledger WHERE seed_key='reference-data.unit-of-measure.system' AND seed_version='1'"),
            await CountRowsAsync(connection, "reference_data.dictionary_definition"),
            await CountRowsAsync(connection, "reference_data.unit_of_measure_dimension"),
            await CountRowsAsync(connection, "reference_data.unit_of_measure_unit"),
            await SchemaFingerprintAsync(connection));
    }

    private static async Task<PerServiceSnapshot> ReadPerServiceSnapshotAsync(
        string connectionString,
        string service)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return service switch
        {
            "identity" => new PerServiceSnapshot(
                service,
                await RequiredScalarAsync(connection, "SELECT migration_id || '|' || description || '|' || applied_on::text FROM identity_schema_migrations WHERE migration_id='ID-004-01'"),
                await CountRowsAsync(connection, "identity_user"),
                null,
                null,
                0,
                0,
                0,
                await SchemaFingerprintAsync(connection)),
            "systemdata" => new PerServiceSnapshot(
                service,
                await RequiredScalarAsync(connection, "SELECT migration_id || '|' || description || '|' || applied_on::text FROM system_data_schema_migrations WHERE migration_id='SDM-001-01'"),
                await CountRowsAsync(connection, "system_data_database_environment_policy"),
                null,
                null,
                0,
                0,
                0,
                await SchemaFingerprintAsync(connection)),
            "collaboration" => new PerServiceSnapshot(
                service,
                await RequiredScalarAsync(connection, "SELECT migration_id || '|' || description || '|' || applied_on::text FROM collaboration_schema_migrations WHERE migration_id='PF05-001'"),
                await CountRowsAsync(connection, "collaboration_conversation"),
                null,
                null,
                0,
                0,
                0,
                await SchemaFingerprintAsync(connection)),
            "referencedata" => new PerServiceSnapshot(
                service,
                await RequiredScalarAsync(connection, "SELECT applied_on::text FROM reference_data.schema_migrations WHERE migration_id='reference-data-2.7-001'"),
                await CountRowsAsync(connection, "reference_data.dictionary_definition"),
                await RequiredScalarAsync(connection, "SELECT applied_on::text FROM reference_data.seed_ledger WHERE seed_key='reference-data.baseline' AND seed_version='reference-data-2.7-001'"),
                await RequiredScalarAsync(connection, "SELECT applied_on::text FROM reference_data.seed_ledger WHERE seed_key='reference-data.unit-of-measure.system' AND seed_version='1'"),
                await CountRowsAsync(connection, "reference_data.unit_of_measure_dimension"),
                await CountRowsAsync(connection, "reference_data.unit_of_measure_unit"),
                0,
                await SchemaFingerprintAsync(connection)),
            _ => throw new ArgumentOutOfRangeException(nameof(service), service, "未知 PerService 快照服务。"),
        };
    }

    private static void AssertPerServiceSnapshotPreserved(
        PerServiceSnapshot before,
        PerServiceSnapshot after) =>
        Assert.Equal(before with { SchemaFingerprint = after.SchemaFingerprint }, after);

    private static async Task AssertPerServiceMetadataAsync(string connectionString, string service)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        switch (service)
        {
            case "identity":
                Assert.Equal(1, await CountAsync(connection, "identity_schema_migrations", "checksum"));
                Assert.False(await IsNullAsync(connection, "identity_schema_migrations", "ID-004-01", "checksum"));
                break;
            case "systemdata":
                Assert.Equal(1, await CountAsync(connection, "system_data_schema_migrations", "checksum"));
                Assert.False(await IsNullAsync(connection, "system_data_schema_migrations", "SDM-001-01", "checksum"));
                break;
            case "collaboration":
                Assert.Equal(1, await CountAsync(connection, "collaboration_schema_migrations", "checksum"));
                Assert.False(await IsNullAsync(connection, "collaboration_schema_migrations", "PF05-001", "checksum"));
                break;
            case "referencedata":
                Assert.Equal(1, await CountAsync(connection, "reference_data.schema_migrations", "checksum"));
                Assert.Equal(1, await CountAsync(connection, "reference_data.seed_ledger", "checksum"));
                Assert.True(await BooleanScalarAsync(connection, "SELECT checksum IS NOT NULL AND target_identity IS NOT NULL FROM reference_data.schema_migrations WHERE migration_id='reference-data-2.7-001'"));
                Assert.True(await BooleanScalarAsync(connection, "SELECT checksum IS NOT NULL AND scope IS NOT NULL FROM reference_data.seed_ledger WHERE seed_key='reference-data.baseline' AND seed_version='reference-data-2.7-001'"));
                Assert.True(await BooleanScalarAsync(connection, "SELECT checksum IS NOT NULL AND scope IS NOT NULL FROM reference_data.seed_ledger WHERE seed_key='reference-data.unit-of-measure.system' AND seed_version='1'"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(service), service, "未知 PerService 元数据服务。");
        }
    }

    private static async Task<string> RequiredScalarAsync(NpgsqlConnection connection, string sql)
    {
        var value = await ScalarAsync(connection, sql);
        return value ?? "<null>";
    }

    private static async Task<string?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<bool> BooleanScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToBoolean(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountRowsAsync(NpgsqlConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {QualifiedIdentifier(table)}";
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountRowsAsync(NpgsqlConnection connection, string table, string migrationId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {QualifiedIdentifier(table)} WHERE migration_id=@migrationId";
        command.Parameters.AddWithValue("migrationId", migrationId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<bool> IsNullAsync(NpgsqlConnection connection, string table, string migrationId, string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {QuoteIdentifier(column)} IS NULL FROM {QualifiedIdentifier(table)} WHERE migration_id=@migrationId";
        command.Parameters.AddWithValue("migrationId", migrationId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<string> SchemaFingerprintAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT table_schema,table_name,column_name,data_type
            FROM information_schema.columns
            WHERE table_schema IN ('public','reference_data')
            ORDER BY table_schema,table_name,ordinal_position;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
            columns.Add($"{reader.GetString(0)}.{reader.GetString(1)}:{reader.GetString(2)}:{reader.GetString(3)}");
        return string.Join('|', columns);
    }

    private static async Task CreateSchemaAsync(string connectionString, string schema)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE SCHEMA {QuoteIdentifier(schema)}";
        await command.ExecuteNonQueryAsync();
    }

    private static string ConnectionForSchema(string connectionString, string schema) =>
        new NpgsqlConnectionStringBuilder(connectionString)
        {
            SearchPath = schema,
        }.ConnectionString;

    private static string QualifiedIdentifier(string value) =>
        string.Join(".", value.Split('.', 2).Select(QuoteIdentifier));

    private static string Checksum(string id, string description) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{id}|{description}"))).ToLowerInvariant();

    private sealed class PostgreSqlIdentityPermissionRegistry(string identityConnectionString) : IIdentityPermissionRegistry
    {
        public async Task<PermissionRegistrationReceipt?> VerifyAsync(
            PermissionManifestV1 manifest,
            CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(identityConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT n_id FROM identity_permission";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var permissionNIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(cancellationToken))
                permissionNIds.Add(reader.GetString(0));

            if (manifest.Permissions.Any(permission => !permissionNIds.Contains(permission.PermissionNId)))
                return null;

            return new PermissionRegistrationReceipt(
                manifest.ModuleNId,
                manifest.ManifestVersion,
                manifest.Checksum,
                true,
                DateTimeOffset.UtcNow);
        }
    }

    private sealed class RecordingInitializationInvoker(
        IServiceInitializationInvoker inner) : IServiceInitializationInvoker
    {
        public int ApplyCount { get; private set; }

        public Task<ServiceInitializationState> InspectAsync(ServiceInitializationContext context, CancellationToken cancellationToken) =>
            inner.InspectAsync(context, cancellationToken);

        public Task<ServiceInitializationPlan> PlanAsync(
            ServiceInitializationContext context,
            ServiceInitializationState inspection,
            CancellationToken cancellationToken) =>
            inner.PlanAsync(context, inspection, cancellationToken);

        public async Task<ServiceInitializationState> ApplyAsync(
            ServiceInitializationContext context,
            ServiceInitializationPlan plan,
            CancellationToken cancellationToken)
        {
            ApplyCount++;
            return await inner.ApplyAsync(context, plan, cancellationToken);
        }

        public Task<ServiceInitializationState> VerifyAsync(ServiceInitializationContext context, CancellationToken cancellationToken) =>
            inner.VerifyAsync(context, cancellationToken);
    }

    private sealed record LegacySnapshot(
        string IdentityLedger,
        int IdentityUserCount,
        string SystemDataLedger,
        int EnvironmentPolicyCount,
        string CollaborationLedger,
        int ConversationCount,
        string ReferenceBaselineAppliedOn,
        string ReferenceBaselineSeedAppliedOn,
        string ReferenceUnitSeedAppliedOn,
        int ReferenceDictionaryCount,
        int ReferenceDimensionCount,
        int ReferenceUnitCount,
        string SchemaFingerprint);

    private sealed record PerServiceSnapshot(
        string Service,
        string PrimaryLedgerFact,
        int PrimaryBusinessCount,
        string? SecondaryLedgerFactA,
        string? SecondaryLedgerFactB,
        int SecondaryBusinessCountA,
        int SecondaryBusinessCountB,
        int SecondaryBusinessCountC,
        string SchemaFingerprint);

    private static ServiceInitializationContext CreateContext(string database, string operation) => new(
        "Development",
        "tenant-pf05-legacy",
        operation,
        "unifiedhost",
        "unifiedhost",
        new ResolvedDatabaseTarget(
            "Development",
            DatabaseTopologyMode.Shared,
            "unifiedhost",
            DatabaseProvider.PostgreSQL,
            "unifiedhost_db",
            database,
            true),
        string.Empty,
        ServiceInitializationPolicy.Standard,
        "trace-pf05-legacy");

    private static string ReadConnectionString(string databaseEnvironmentVariable)
    {
        var host = Required("PF05_STARTUP_CHAIN_PG_HOST");
        var port = int.Parse(Required("PF05_STARTUP_CHAIN_PG_PORT"), CultureInfo.InvariantCulture);
        var database = Required(databaseEnvironmentVariable);
        var username = Required("PF05_STARTUP_CHAIN_PG_USERNAME");
        var password = Required("PF05_STARTUP_CHAIN_PG_PASSWORD");
        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            Password = password,
            SearchPath = "public",
            Pooling = false,
            Timeout = 10,
            CommandTimeout = 30,
        }.ConnectionString;
    }

    private static string CreatePhysicalConnectionString(string baseConnectionString, string database) =>
        new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = database,
            SearchPath = "public",
            Pooling = false,
            Timeout = 10,
            CommandTimeout = 30,
        }.ConnectionString;

    private static async Task CreateDatabaseAsync(string adminConnectionString, string database)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE {QuoteIdentifier(database)}";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string adminConnectionString, string database)
    {
        try
        {
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS {QuoteIdentifier(database)} WITH (FORCE)";
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception exception) when (exception is not XunitException)
        {
            throw new Xunit.Sdk.XunitException($"隔离 PostgreSQL 临时库清理失败:{exception.GetType().Name}:{exception.Message}");
        }
    }

    private static async Task<IReadOnlyList<string>> DropDatabasesBestEffortAsync(
        string adminConnectionString,
        IEnumerable<string> databases)
    {
        var failures = new List<string>();
        foreach (var database in databases.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await DropDatabaseAsync(adminConnectionString, database);
            }
            catch (Exception exception)
            {
                failures.Add($"{database}:{exception.GetType().Name}:{exception.Message}");
            }
        }

        return failures;
    }

    private static async Task<int> CountAsync(NpgsqlConnection connection, string table, string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = @schema AND table_name = @table AND column_name = @column";
        var parts = table.Split('.', 2);
        command.Parameters.AddWithValue("schema", parts.Length == 2 ? parts[0] : "public");
        command.Parameters.AddWithValue("table", parts.Length == 2 ? parts[1] : parts[0]);
        command.Parameters.AddWithValue("column", column);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string schema, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema=@schema AND table_name=@table);
            """;
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new Xunit.Sdk.XunitException($"未配置 {name}。");

    private static string QuoteIdentifier(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
