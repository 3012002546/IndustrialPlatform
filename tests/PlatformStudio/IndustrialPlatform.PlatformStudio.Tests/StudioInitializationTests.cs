using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class StudioInitializationTests
{
    [Fact]
    public async Task Cold_embedded_sqlite_creates_only_selected_runtime_tables_and_second_start_is_idempotent()
    {
        var folder = Path.Combine(Path.GetTempPath(), "studio-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var profile = Profile(Path.Combine(folder, "studio.db"));
            using var db = new StudioDatabase(profile, new ConfigurationBuilder().Build());
            var initializer = new StudioServiceInitializer(db, profile, TimeProvider.System);
            Assert.False((await initializer.InspectLocalAsync(CancellationToken.None)).Ready);
            Assert.False(File.Exists(Path.Combine(folder, "studio.db.scheduler.db")));
            await initializer.InitializeLocalAsync(CancellationToken.None);
            Assert.True((await initializer.InspectLocalAsync(CancellationToken.None)).Ready);
            Assert.Equal(16, StudioSchema.Core.Count);
            foreach (var table in StudioSchema.Core) Assert.True(await db.TableExistsAsync(table.Name, CancellationToken.None));
            Assert.True(await db.TableExistsAsync("system_audit_fact", CancellationToken.None));
            Assert.False(await db.TableExistsAsync("system_file_object", CancellationToken.None));
            Assert.False(await db.TableExistsAsync("system_notification_message", CancellationToken.None));
            Assert.False(await db.TableExistsAsync("system_data_database_operation", CancellationToken.None));
            Assert.False(await db.TableExistsAsync("system_collaboration_file_reference", CancellationToken.None));
            Assert.False(await db.TableExistsAsync("scheduler_job", CancellationToken.None));
            var before = await db.Client.Ado.GetIntAsync("SELECT COUNT(*) FROM studio_schema_migrations");
            await initializer.InitializeLocalAsync(CancellationToken.None);
            Assert.Equal(before, await db.Client.Ado.GetIntAsync("SELECT COUNT(*) FROM studio_schema_migrations"));
            Assert.Equal(7, before);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task Migration_checksum_drift_fails_closed_and_inspect_does_not_repair_it()
    {
        var folder = Path.Combine(Path.GetTempPath(), "studio-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var profile = Profile(Path.Combine(folder, "studio.db"));
            using var db = new StudioDatabase(profile, new ConfigurationBuilder().Build());
            var initializer = new StudioServiceInitializer(db, profile, TimeProvider.System);
            await initializer.InitializeLocalAsync(CancellationToken.None);
            await db.Client.Ado.ExecuteCommandAsync("UPDATE studio_schema_migrations SET checksum='drift' WHERE migration_id='STU-001-Resources'");
            var state = await initializer.InspectLocalAsync(CancellationToken.None);
            Assert.False(state.Ready);
            Assert.Contains("DRIFT", state.Reason);
            await Assert.ThrowsAsync<StudioException>(() => initializer.InitializeLocalAsync(CancellationToken.None));
            Assert.Equal("drift", await db.Client.Ado.GetStringAsync("SELECT checksum FROM studio_schema_migrations WHERE migration_id='STU-001-Resources'"));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    internal static StudioDeploymentProfile Profile(string file) => new()
    {
        Enabled = true, InstalledModules = ["LowCode"], DeploymentMode = "ExternalMes",
        Database = new() { Provider = "Sqlite", SqliteFile = file }, PublicOrigin = "https://studio.example.invalid",
        Identity = new() { Provider = "LocalAdmin", LocalAdminEnabled = true, IssuerKey = "studio-local" },
        Files = new() { Provider = "Disabled" }, Audit = new() { Provider = "Embedded" }, Jobs = new() { Provider = "Disabled" },
    };
}
