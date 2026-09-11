using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Application.Files;
using IndustrialPlatform.SystemData.Application.Pf04;
using IndustrialPlatform.SystemData.Contracts.Files;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.SystemData;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;

namespace IndustrialPlatform.SystemData.Tests;

public sealed class Pf04MigrationTests
{
    [Fact]
    public void MigrationCatalog_ContainsCoreFileNotificationAuditTables()
    {
        var descriptions = SystemDataSchemaMigrations.All
            .Select(step => step.Description)
            .ToArray();

        Assert.Contains("system_file_upload_session", descriptions);
        Assert.Contains("system_file_object", descriptions);
        Assert.Contains("system_file_scan_attempt", descriptions);
        Assert.Contains("system_file_reference_grant", descriptions);
        Assert.Contains("system_notification_announcement", descriptions);
        Assert.Contains("system_notification_message", descriptions);
        Assert.Contains("system_notification_inbox_delivery", descriptions);
        Assert.Contains("system_audit_fact", descriptions);
        Assert.Contains("system_audit_lifecycle", descriptions);
        Assert.Contains("system_audit_ingress_failure", descriptions);
        Assert.Contains("system_audit_outbox", descriptions);
        Assert.Contains("system_audit_lifecycle legal hold", descriptions);
    }

    [Fact]
    public async Task Sql_file_store_persists_dedicated_reference_and_hold_and_closes_the_delete_race()
    {
        Batteries_V2.Init();
        var path = Path.Combine(Path.GetTempPath(), $"pf05-file-boundary-{Guid.NewGuid():N}.db");
        using var context = new SqlSugarDbContext(Options.Create(new SqlSugarOptions { ConnectionString = $"Data Source={path}", DbType = DbType.Sqlite }));
        await new SchemaMigrationRunner(context, SystemDataSchemaMigrations.All, Microsoft.Extensions.Logging.Abstractions.NullLogger<SchemaMigrationRunner>.Instance).ApplyPendingAsync();
        var now = DateTimeOffset.UtcNow;
        await context.SqlSugar.Insertable(new FileObjectTable
        {
            Id = Guid.NewGuid(), TenantNId = "tenant-1", FileNId = "file-1", UploadSessionNId = "session-1", OwnerUserNId = "user-1",
            FileName = "a.txt", ContentType = "text/plain", ContentLength = 2, Sha256 = "ab", StorageKey = "tenant-1/session-1.bin",
            ScanStatus = "Clean", DeletionStatus = "Active", CreatedOn = now, LastUpdatedOn = now,
        }).ExecuteCommandAsync();

        var store = new Pf04Store(context);
        var service = new FileService(store, new NoopContentStore(), TimeProvider.System);
        var checksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("scope"u8.ToArray())).ToLowerInvariant();
        var binding = await service.BindReferenceAsync("tenant-1", "user-1", "REF-1", new FileBindingRequest
        {
            FileNId = "file-1", ConversationNId = "conv-1", MessageNId = "msg-1", AttachmentNId = "att-1", UploaderUserNId = "user-1",
            Purpose = "CollaborationMessageAttachment", RequestNId = "REQ-REF",
        }, CancellationToken.None);
        Assert.Equal(0, binding.Version);
        var hold = await service.PutLegalHoldAsync("tenant-1", "user-1", "CASE-A", "file-1", new FileHoldRequest { RequestNId = "REQ-A", ScopeChecksum = checksum, CaseRevision = 1 }, CancellationToken.None);
        Assert.Equal("Active", hold.Status);
        Assert.NotNull(await service.GetLegalHoldAsync("tenant-1", "user-1", "CASE-A", "file-1", CancellationToken.None));

        var blocked = await Assert.ThrowsAsync<Pf04ServiceException>(() => service.RequestDeletionAsync("tenant-1", "user-1", "file-1", CancellationToken.None));
        Assert.Equal("FILE_LEGAL_HOLD_ACTIVE", blocked.Code);
        await service.ReleaseLegalHoldAsync("tenant-1", "user-1", "CASE-A", "file-1", new FileHoldRequest { RequestNId = "REQ-RA", ScopeChecksum = checksum, CaseRevision = 1 }, CancellationToken.None);
        blocked = await Assert.ThrowsAsync<Pf04ServiceException>(() => service.RequestDeletionAsync("tenant-1", "user-1", "file-1", CancellationToken.None));
        Assert.Equal("FILE_REFERENCED", blocked.Code);
        await service.ReleaseReferenceAsync("tenant-1", "user-1", string.Empty, "REF-1", new FileBindingReleaseRequest { RequestNId = "REQ-REF-RELEASE", ExpectedVersion = 0 }, CancellationToken.None);
        Assert.Equal("DeletionRequested", (await service.RequestDeletionAsync("tenant-1", "user-1", "file-1", CancellationToken.None)).DeletionStatus);

        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }

    private sealed class NoopContentStore : IFileContentStore
    {
        public Task<long> AppendAsync(string storageKey, long expectedOffset, Stream content, CancellationToken cancellationToken) => Task.FromResult(expectedOffset);
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) => Task.FromResult<Stream>(new MemoryStream());
        public Task<string> ComputeSha256Async(string storageKey, CancellationToken cancellationToken) => Task.FromResult(string.Empty);
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
