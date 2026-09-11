using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.Collaboration.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Infrastructure_SqliteUtcTimestampTests
{
    [Fact]
    public async Task Sqlite_round_trip_preserves_utc_semantics_for_hold_and_export_deadlines()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"pf05-collaboration-utc-{Guid.NewGuid():N}.db");
        SqlSugarDbContext? context = null;
        try
        {
            context = CreateContext(databasePath);
            foreach (var migration in CollaborationSchemaMigrations.All)
                await migration.Apply(context.SqlSugar, CancellationToken.None);

            var repository = new SqlCollaborationRepository(context);
            var now = DateTimeOffset.UtcNow;
            var hold = new LegalHoldRecord(
                "T-1", "HLD-1", "ReleasePendingApproval", "{\"conversationNId\":\"CV-1\"}", "scope-hash", "investigation", "U-1",
                now, null, 2, Guid.NewGuid(), "REQ-HOLD", "hold-hash", "OP-HOLD", "U-1", "REQ-RELEASE", now.AddMinutes(15), null, "U-2", now, "CASE-1", "Synchronized");
            var export = new ComplianceExportRecord(
                "T-1", "EXP-1", "Running", "{\"scope\":{}}", "scope-hash", "investigation", "U-1",
                now, now.AddHours(24), "FILE-1|REF-1", 3, Guid.NewGuid(), "REQ-EXPORT", "export-hash", "CASE-1", "U-2",
                now.AddMinutes(-1), now.AddMinutes(14), now.AddMinutes(1), now.AddMinutes(30), null, null, null, "OP-EXPORT", 24, "WRK-1", now.AddMinutes(2));

            await repository.CreateLegalHoldAsync(hold, CancellationToken.None);
            await repository.CreateExportAsync(export, CancellationToken.None);

            var storedHold = await repository.GetLegalHoldAsync("T-1", "HLD-1", CancellationToken.None);
            var storedExport = await repository.GetExportAsync("T-1", "EXP-1", CancellationToken.None);

            Assert.NotNull(storedHold);
            var savedHold = storedHold!;
            Assert.NotNull(savedHold.ReleaseApprovalExpiresOn);
            Assert.Equal(TimeSpan.Zero, savedHold.ReleaseApprovalExpiresOn!.Value.Offset);
            Assert.Equal(hold.ReleaseApprovalExpiresOn!.Value, savedHold.ReleaseApprovalExpiresOn.Value);
            Assert.NotNull(storedExport);
            var savedExport = storedExport!;
            Assert.Equal(TimeSpan.Zero, savedExport.ApprovalExpiresOn!.Value.Offset);
            Assert.Equal(TimeSpan.Zero, savedExport.ExpiresOn!.Value.Offset);
            Assert.Equal(TimeSpan.Zero, savedExport.RunDeadlineOn!.Value.Offset);
            Assert.Equal(TimeSpan.Zero, savedExport.WorkerLeaseUntil!.Value.Offset);
            Assert.Equal(export.ApprovalExpiresOn!.Value, savedExport.ApprovalExpiresOn.Value);
            Assert.Equal(export.ExpiresOn!.Value, savedExport.ExpiresOn.Value);
            Assert.Equal(export.RunDeadlineOn!.Value, savedExport.RunDeadlineOn.Value);
            Assert.Equal(export.WorkerLeaseUntil!.Value, savedExport.WorkerLeaseUntil.Value);
        }
        finally
        {
            context?.Dispose();
            DeleteDatabase(databasePath);
        }
    }

    private static SqlSugarDbContext CreateContext(string databasePath) =>
        new(Options.Create(new SqlSugarOptions { DbType = DbType.Sqlite, ConnectionString = $"Data Source={databasePath}", IsAutoCloseConnection = true }));

    private static void DeleteDatabase(string databasePath)
    {
        if (!File.Exists(databasePath))
            return;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                File.Delete(databasePath);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(25);
            }
            catch (IOException)
            {
                return;
            }
        }
    }
}
