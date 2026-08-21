using System.Reflection;
using SqlSugar;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;

namespace IndustrialPlatform.SystemData.Infrastructure.Tests;

public sealed class ControlPlaneSchemaTests
{
    [Fact]
    public void PostgreSql_control_plane_ddl_uses_provider_native_temporal_and_guid_types()
    {
        var projection = Ddl("ProjectionRevisionDdl", DbType.PostgreSQL);
        var navigationSnapshot = Ddl("NavigationSnapshotDdl", DbType.PostgreSQL);
        var manifest = Ddl("ModuleManifestDdl", DbType.PostgreSQL);
        var audit = Ddl("AuditDdl", DbType.PostgreSQL);
        var outbox = Ddl("OutboxDdl", DbType.PostgreSQL);
        var seedLedger = Ddl("SeedLedgerDdl", DbType.PostgreSQL);
        var upgrade = Ddl("PostgreSqlControlPlaneNativeTypesDdl", DbType.PostgreSQL);

        Assert.Contains("generated_on TIMESTAMPTZ", projection, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("published_on TIMESTAMPTZ", navigationSnapshot, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("permission_verified_on TIMESTAMPTZ", manifest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("event_id uuid", outbox, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("event_created_time TIMESTAMPTZ", outbox, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("published_on TIMESTAMPTZ", outbox, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("applied_on TIMESTAMPTZ", seedLedger, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actor_user_n_id TEXT", audit, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ALTER TABLE system_data_outbox ALTER COLUMN event_id TYPE uuid", upgrade, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sqlite_control_plane_ddl_keeps_text_storage_for_temporal_and_guid_values()
    {
        var projection = Ddl("ProjectionRevisionDdl", DbType.Sqlite);
        var navigationSnapshot = Ddl("NavigationSnapshotDdl", DbType.Sqlite);
        var outbox = Ddl("OutboxDdl", DbType.Sqlite);

        Assert.Contains("generated_on TEXT", projection, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("published_on TEXT", navigationSnapshot, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("event_id TEXT", outbox, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("event_created_time TEXT", outbox, StringComparison.OrdinalIgnoreCase);
    }

    private static string Ddl(string methodName, DbType dbType) =>
        (string)typeof(SystemDataSchemaMigrations)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [dbType])!;
}
