using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Application.Reliability;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.SystemData;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using RabbitMQ.Client;
using SqlSugar;
using StackExchange.Redis;
using Xunit.Sdk;

namespace IndustrialPlatform.IntegrationTests.SystemData;

/// <summary>
/// TASK-SD-010 真实控制面依赖门禁。
/// <para>
/// 只有显式设置 <c>SYSTEMDATA_CONTROL_PLANE_E2E=1</c> 才执行；未设置时标记为 Skip，
/// 不以空方法返回伪造通过。真实连接参数只从环境变量读取，测试输出不打印其值。
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public sealed class SystemDataControlPlaneDependenciesE2ETests
{
    private const string Gate = "SYSTEMDATA_CONTROL_PLANE_E2E";
    private static readonly string[] ControlPlaneTables =
    [
        "system_data_module_manifest",
        "system_data_ui_resource",
        "system_data_navigation_set",
        "system_data_navigation",
        "system_data_navigation_snapshot",
        "system_data_feature_override",
        "system_data_feature",
        "system_data_service_catalog",
        "system_data_theme_policy",
        "system_data_projection_revision",
        "system_data_operation_audit",
        "system_data_outbox",
    ];

    [SystemDataControlPlaneFact]
    public async Task PostgreSql_control_plane_store_commits_and_reads_real_rows()
    {
        var connectionString = PostgreSqlConnectionString();
        var tenantNId = $"tenant-sd010-{Guid.NewGuid():N}";
        var schemaReady = false;

        try
        {
            using var db = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
            {
                ConnectionString = connectionString,
                DbType = DbType.PostgreSQL,
            }));
            var runner = new SchemaMigrationRunner(
                db,
                SystemDataSchemaMigrations.All.Select(x => new SchemaMigrationStep(x.Id, x.Description, x.Apply)),
                NullLogger<SchemaMigrationRunner>.Instance);
            await runner.ApplyPendingAsync();
            schemaReady = true;

            var eventId = Guid.NewGuid();
            var store = new SqlControlPlaneStore(db);
            var revision = await store.CommitAsync(
                ControlPlaneSnapshot.Empty(tenantNId),
                expectedRevision: 0,
                new ControlPlaneCommit(
                    [new ControlPlaneEvent(eventId, "SystemData.SD010.Probe.v1", "v1", tenantNId, "{}", DateTimeOffset.UtcNow)],
                    [new LocalAuditEntry(tenantNId, "sd010-test", "sd010.probe", "ControlPlane", tenantNId, null, null, "real-postgresql", "sd010-test")]),
                CancellationToken.None);

            Assert.Equal(1, revision);
            var loaded = await new SqlControlPlaneStore(db).LoadAsync(tenantNId, CancellationToken.None);
            Assert.Equal(1, loaded.Revision);

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    (SELECT COUNT(1) FROM system_data_operation_audit WHERE tenant_n_id = @tenant),
                    (SELECT COUNT(1) FROM system_data_outbox WHERE tenant_n_id = @tenant AND event_id = @event_id);
                """;
            command.Parameters.AddWithValue("tenant", tenantNId);
            command.Parameters.AddWithValue("event_id", eventId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.Equal(1L, reader.GetInt64(1));
        }
        catch (XunitException)
        {
            throw;
        }
        catch (PostgresException exception)
        {
            var latestMigration = await LatestMigrationIdAsync(connectionString);
            var emptyTimestampFields = await EmptyTimestampFieldsAsync(connectionString);
            throw new XunitException($"真实 PostgreSQL 控制面门禁失败:PostgresException SQLSTATE={exception.SqlState}; latestMigration={latestMigration}; emptyTimestampFields={emptyTimestampFields}; {exception.MessageText}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new XunitException($"真实 PostgreSQL 控制面门禁失败:{exception.GetType().Name}");
        }
        finally
        {
            if (schemaReady)
            {
                await DeleteTenantRowsAsync(connectionString, tenantNId);
            }
        }
    }

    [SystemDataControlPlaneFact]
    public async Task Redis_control_plane_versioned_snapshot_round_trip_is_real()
    {
        var connectionString = Required("SYSTEMDATA_CONTROL_PLANE_REDIS_CONNECTION");
        var key = $"systemdata:control-plane:sd010:{Guid.NewGuid():N}:v1";

        try
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;
            await using var connection = await ConnectionMultiplexer.ConnectAsync(options);
            var database = connection.GetDatabase();
            await database.StringSetAsync(key, "sd010", TimeSpan.FromMinutes(15));
            Assert.Equal("sd010", await database.StringGetAsync(key));
            var ttl = await database.KeyTimeToLiveAsync(key);
            Assert.True(ttl is { } value && value > TimeSpan.FromMinutes(14), $"版本化快照 TTL 异常:{ttl}");
            await database.KeyDeleteAsync(key);
        }
        catch (XunitException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new XunitException($"真实 Redis 控制面门禁失败:{exception.GetType().Name}");
        }
    }

    [SystemDataControlPlaneFact]
    public async Task RabbitMq_control_plane_channel_can_be_opened()
    {
        var factory = new ConnectionFactory
        {
            HostName = Required("SYSTEMDATA_CONTROL_PLANE_RABBIT_HOST"),
            Port = PositivePort("SYSTEMDATA_CONTROL_PLANE_RABBIT_PORT", 5672),
            UserName = Required("SYSTEMDATA_CONTROL_PLANE_RABBIT_USERNAME"),
            Password = Required("SYSTEMDATA_CONTROL_PLANE_RABBIT_PASSWORD"),
            VirtualHost = Required("SYSTEMDATA_CONTROL_PLANE_RABBIT_VHOST"),
        };

        try
        {
            await using var connection = await factory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();
            var queue = await channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true);
            Assert.False(string.IsNullOrWhiteSpace(queue.QueueName));
        }
        catch (XunitException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new XunitException($"真实 RabbitMQ 控制面门禁失败:{exception.GetType().Name}");
        }
    }

    private static string PostgreSqlConnectionString() => new NpgsqlConnectionStringBuilder
    {
        Host = Required("SYSTEMDATA_CONTROL_PLANE_PG_HOST"),
        Port = PositivePort("SYSTEMDATA_CONTROL_PLANE_PG_PORT", 5432),
        Database = Required("SYSTEMDATA_CONTROL_PLANE_PG_DATABASE"),
        Username = Required("SYSTEMDATA_CONTROL_PLANE_PG_USERNAME"),
        Password = Required("SYSTEMDATA_CONTROL_PLANE_PG_PASSWORD"),
        SearchPath = "public",
    }.ConnectionString;

    private static async Task DeleteTenantRowsAsync(string connectionString, string tenantNId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = string.Join(Environment.NewLine, ControlPlaneTables.Select(table => $"DELETE FROM {table} WHERE tenant_n_id = @tenant;"));
        command.Parameters.AddWithValue("tenant", tenantNId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> LatestMigrationIdAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(migration_id), '<none>') FROM system_data_schema_migrations";
        return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? "<none>";
    }

    private static async Task<string> EmptyTimestampFieldsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var candidates = new (string Table, string Column)[]
        {
            ("system_data_module_manifest", "permission_verified_on"),
            ("system_data_navigation_snapshot", "published_on"),
            ("system_data_projection_revision", "generated_on"),
            ("system_data_outbox", "event_created_time"),
            ("system_data_outbox", "published_on"),
            ("system_data_outbox", "next_attempt_on"),
            ("system_data_outbox", "dead_on"),
            ("system_data_seed_ledger", "applied_on"),
        };
        var found = new List<string>();
        foreach (var candidate in candidates)
        {
            await using var typeCommand = connection.CreateCommand();
            typeCommand.CommandText = "SELECT data_type FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @table AND column_name = @column";
            typeCommand.Parameters.AddWithValue("table", candidate.Table);
            typeCommand.Parameters.AddWithValue("column", candidate.Column);
            var dataType = Convert.ToString(await typeCommand.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
            if (dataType is not ("text" or "character varying")) continue;
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT EXISTS (SELECT 1 FROM {candidate.Table} WHERE {candidate.Column} = '')";
            if (await command.ExecuteScalarAsync() is true) found.Add($"{candidate.Table}.{candidate.Column}");
        }
        return found.Count == 0 ? "<none>" : string.Join(',', found);
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"真实控制面门禁缺少环境变量:{name}");

    private static int PositivePort(string name, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : int.TryParse(value, out var port) && port is > 0 and <= 65535
                ? port
                : throw new InvalidOperationException($"真实控制面门禁端口无效:{name}");
    }
}

internal sealed class SystemDataControlPlaneFactAttribute : FactAttribute
{
    public SystemDataControlPlaneFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SYSTEMDATA_CONTROL_PLANE_E2E"), "1", StringComparison.Ordinal))
        {
            Skip = "未设置 SYSTEMDATA_CONTROL_PLANE_E2E=1；真实控制面依赖门禁未执行，不计入通过。";
        }
    }
}
