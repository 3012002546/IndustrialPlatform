using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class StudioInitializationTests
{
    [Fact]
    public async Task Workbook_working_copy_upgrade_preserves_existing_history_and_session_revision()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var db = fixture.Database;
        var initializer = new StudioServiceInitializer(db, fixture.Profile, TimeProvider.System);
        var now = DateTimeOffset.UtcNow;
        var workbook = new StudioWorkbook(now) { NId = "legacy-book", TenantNId = "tenant-a", Name = "Legacy", OwnerUserNId = "user-a", VersionSequence = 5 };
        await db.Client.Insertable(workbook).ExecuteCommandAsync();
        var versions = Enumerable.Range(1, 5).Select(i => new StudioWorkbookVersion
        {
            Id = Guid.NewGuid(), NId = "legacy-version-" + i, TenantNId = workbook.TenantNId, WorkbookId = workbook.Id,
            ContentVersion = i, HistoryVersion = i, PinnedOn = now, CreatedOn = now,
            FileNId = "file-" + i, FileReferenceNId = "reference-" + i, Sha256 = i.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(64, '0'),
            SavedBy = "user-a", SaveOperationKey = "save-" + i,
        }).ToArray();
        await db.Client.Insertable(versions).ExecuteCommandAsync();
        workbook.CurrentVersionId = versions[^1].Id;
        await db.Client.Updateable(workbook).ExecuteCommandAsync();
        var session = new StudioWorkbookSession
        {
            Id = Guid.NewGuid(), NId = "legacy-session", TenantNId = workbook.TenantNId, CreatedOn = now,
            WorkbookId = workbook.Id, BaseVersionId = versions[2].Id, UserNId = "user-a", ProviderKey = "test",
            AccessTokenHash = "keep-token-hash", ExpiresOn = now.AddHours(1), LastSeenOn = now, IdentityEvidenceJson = "{}",
        };
        await db.Client.Insertable(session).ExecuteCommandAsync();
        var oldChecksum = await db.Client.Ado.GetStringAsync("SELECT checksum FROM studio_schema_migrations WHERE migration_id='STU-005-Workbook'");
        Assert.False(string.IsNullOrEmpty(oldChecksum));
        // Restore the pre-upgrade physical schema, including five immutable historical files.
        await db.Client.Ado.ExecuteCommandAsync("DROP INDEX uq_st_workbook_history");
        await db.Client.Ado.ExecuteCommandAsync("DROP TABLE studio_workbook_retired_file");
        foreach (var (table, columns) in new[] {
            ("studio_workbook", new[] { "history_sequence", "has_working_copy" }),
            ("studio_workbook_version", new[] { "history_version", "pinned_on", "version_note" }),
            ("studio_workbook_session", new[] { "base_content_version" }) })
            foreach (var column in columns)
                await db.Client.Ado.ExecuteCommandAsync($"ALTER TABLE {db.Table(table)} DROP COLUMN {column}");
        await db.Client.Ado.ExecuteCommandAsync("DELETE FROM studio_schema_migrations WHERE migration_id='STU-009-WorkbookWorkingCopy'");
        Assert.False((await initializer.InspectLocalAsync(CancellationToken.None)).Ready);
        await initializer.InitializeLocalAsync(CancellationToken.None);
        await initializer.InitializeLocalAsync(CancellationToken.None);
        Assert.True((await initializer.InspectLocalAsync(CancellationToken.None)).Ready);
        var upgraded = await db.Client.Queryable<StudioWorkbook>().InSingleAsync(workbook.Id);
        Assert.Equal(5, upgraded.HistorySequence);
        Assert.False(upgraded.HasWorkingCopy);
        Assert.Equal(workbook.CurrentVersionId, upgraded.CurrentVersionId);
        var history = await db.Client.Queryable<StudioWorkbookVersion>().Where(v => v.WorkbookId == workbook.Id).OrderBy(v => v.ContentVersion).ToListAsync();
        Assert.Equal(5, history.Count);
        for (var i = 0; i < history.Count; i++)
        {
            Assert.Equal(versions[i].HistoryVersion, history[i].HistoryVersion);
            Assert.Equal(versions[i].FileNId, history[i].FileNId);
            Assert.Equal(versions[i].FileReferenceNId, history[i].FileReferenceNId);
            Assert.Equal(versions[i].Sha256, history[i].Sha256);
            Assert.NotNull(history[i].PinnedOn);
        }
        var upgradedSession = await db.Client.Queryable<StudioWorkbookSession>().InSingleAsync(session.Id);
        Assert.Equal(3, upgradedSession.BaseContentVersion);
        Assert.Equal(session.AccessTokenHash, upgradedSession.AccessTokenHash);
        Assert.Equal(oldChecksum, await db.Client.Ado.GetStringAsync("SELECT checksum FROM studio_schema_migrations WHERE migration_id='STU-005-Workbook'"));
    }

    [Fact]
    public async Task Screen_policy_upgrade_preserves_legacy_link_hash_expiry_and_pinned_revision()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var db = fixture.Database;
        var initializer = new StudioServiceInitializer(db, fixture.Profile, TimeProvider.System);
        var store = new StudioScreenStore(db);
        var legacy = new StudioScreenLink { Id = Guid.NewGuid(), TenantNId = "tenant-a", ResourceNId = "old-dashboard", RevisionNId = "old-revision",
            CreatedBy = "user-a", IssuerKey = "test", AuthVersion = "1", ParametersJson = "{}", TokenHash = StudioJson.Hash("legacy-secret"),
            CreatedOn = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero), ExpiresOn = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        await store.InsertAsync(legacy, CancellationToken.None);
        var oldChecksum = await db.Client.Ado.GetStringAsync("SELECT checksum FROM studio_schema_migrations WHERE migration_id='STU-007-DashboardScreen'");
        // 还原升级前的真实物理结构和账本，模拟已发出链接的数据库。
        foreach (var column in new[] { "never_expires", "follow_published" })
            await db.Client.Ado.ExecuteCommandAsync($"ALTER TABLE {db.Table("studio_dashboard_screen")} DROP COLUMN {column}");
        await db.Client.Ado.ExecuteCommandAsync("DELETE FROM studio_schema_migrations WHERE migration_id=@id", new { id = StudioSchema.ScreenPolicy.Step });
        Assert.False((await initializer.InspectLocalAsync(CancellationToken.None)).Ready);
        await initializer.InitializeLocalAsync(CancellationToken.None);
        Assert.True((await initializer.InspectLocalAsync(CancellationToken.None)).Ready);
        var upgraded = (await store.FindAsync(legacy.Id, CancellationToken.None))!;
        Assert.False(upgraded.NeverExpires);
        Assert.False(upgraded.FollowPublished);
        Assert.Equal(legacy.ExpiresOn, upgraded.ExpiresOn);
        Assert.Equal(legacy.RevisionNId, upgraded.RevisionNId);
        Assert.Equal(legacy.TokenHash, upgraded.TokenHash);
        Assert.Equal(oldChecksum, await db.Client.Ado.GetStringAsync("SELECT checksum FROM studio_schema_migrations WHERE migration_id='STU-007-DashboardScreen'"));
        await initializer.InitializeLocalAsync(CancellationToken.None);
    }

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
            Assert.Equal(17, StudioSchema.Core.Count);
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
            Assert.Equal(10, before);
            Assert.True(await db.TableExistsAsync("studio_workbook_retired_file", CancellationToken.None));
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
