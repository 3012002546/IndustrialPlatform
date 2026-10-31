using System.Security.Cryptography;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Api;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;
using IndustrialPlatform.PlatformStudio.Infrastructure.Integration.Embedded;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using IndustrialPlatform.PlatformStudio.Infrastructure.Report;
using IndustrialPlatform.RuntimeSupport.Files;
using IndustrialPlatform.SystemData.Application.Files;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class WorkbookLifecycleTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class ChangeDuringRead(byte[] bytes, Func<Task> change) : MemoryStream(bytes)
    {
        private bool _changed;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_changed) { _changed = true; await change(); }
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
    private sealed class Identity : IStudioIdentity
    {
        public StudioPrincipal Principal { get; set; } = new("tenant-a", "user-a", "test", "test", [], StudioKinds.Permissions, ["factory-a"], "1", "1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
        public Task<StudioPrincipal> GetCurrentAsync(CancellationToken ct) => Task.FromResult(Principal);
        public Task<StudioPrincipal> RefreshAsync(StudioPrincipal principal, CancellationToken ct) => Task.FromResult(Principal);
        public Task<string> CaptureExecutionEvidenceAsync(StudioPrincipal principal, CancellationToken ct) => Task.FromResult("source-session");
        public Task<StudioPrincipal> RefreshExecutionAsync(string evidence, CancellationToken ct) => Task.FromResult(Principal);
    }
    [Fact]
    public async Task Real_file_confirmation_versions_single_lock_conflict_recovery_and_archive_preserve_previous_bytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-workbook-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var profile = StudioInitializationTests.Profile(Path.Combine(root, "studio.db")); profile.Files = new() { Provider = "Embedded", Root = Path.Combine(root, "files") };
            using var database = new StudioDatabase(profile, new ConfigurationBuilder().Build());
            var clock = new Clock();
            var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); services.AddSingleton<TimeProvider>(clock); services.AddSingleton(profile); services.AddSingleton(database); var identity = new Identity(); services.AddSingleton<IStudioIdentity>(identity); services.AddScoped<IStudioResourceStore, StudioResourceStore>(); services.AddScoped<IStudioAuthorization, StudioAuthorization>(); services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(root, "keys"))); services.AddStudioEmbeddedFileAudit(profile); services.AddScoped<StudioWorkbooks>();
            await using var provider = services.BuildServiceProvider(); await new StudioServiceInitializer(database, profile, TimeProvider.System).InitializeLocalAsync(default);
            var files = provider.GetRequiredService<IFileService>(); var fileStore = provider.GetRequiredService<IFileStore>(); var books = provider.GetRequiredService<StudioWorkbooks>(); var p = identity.Principal;
            using var original = ReportPlanningTests.Package(); var hash = Convert.ToHexStringLower(SHA256.HashData(original.ToArray()));
            var session = await files.CreateSessionAsync(p.TenantNId, p.SubjectNId, new() { SessionNId = "source", FileName = "source.xlsx", Length = original.Length, Sha256 = hash, Purpose = "WorkbookImport" }, default);
            session = await files.ResumeProofAsync(p.TenantNId, session.SessionNId, p.SubjectNId, new() { WriterEpoch = session.WriterEpoch, Proof = hash }, default);
            await files.AppendAsync(p.TenantNId, session.TransportId, p.SubjectNId, 0, session.WriterEpoch, original, session.ResumeTicket, default);
            var source = await files.CompleteAsync(p.TenantNId, session.SessionNId, p.SubjectNId, default);
            async Task ScanPending()
            {
                foreach (var file in await fileStore.ListPendingScanAsync(100, default)) await fileStore.UpdateFileAsync(file with { ScanStatus = "Clean" }, default); // Explicit fixture scanner evidence.
            }
            await ScanPending();
            Assert.Equal("WORKBOOK_SCAN_PENDING", (await Assert.ThrowsAsync<StudioException>(() => books.ImportAsync(p, "Book", source.FileNId, "import-one", default))).Code);
            Assert.Empty(await books.ListAsync(p, default)); await ScanPending();
            var workbook = await books.ImportAsync(p, "Book", source.FileNId, "import-one", default); Assert.Equal("1", workbook.ContentVersion);
            Assert.Equal(workbook.NId, (await books.ImportAsync(p, "Book", source.FileNId, "import-one", default)).NId);
            var first = await books.CreateSessionAsync(p, workbook.NId, "Edit", "1", new("https://office.invalid/browser/editor.html"), new("https://studio.invalid/"), default);
            var stale = await books.CreateSessionAsync(p, workbook.NId, "Edit", "1", new("https://office.invalid/browser/editor.html"), new("https://studio.invalid/"), default);
            var view = await books.CreateSessionAsync(p, workbook.NId, "View", "1", new("https://office.invalid/browser/editor.html"), new("https://studio.invalid/"), default);
            // Exercise the actual WOPI endpoint serialization/filter; no Office server is simulated.
            var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Services.AddSingleton(books);
            await using var app = builder.Build(); profile.Office.Admitted = true; app.MapStudioWopi(profile); await app.StartAsync();
            using var client = app.GetTestClient();
            async Task<JsonElement> Info(WorkbookSessionDto editor)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/office/wopi/files/" + editor.SessionNId);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", editor.AccessToken);
                using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.True(response.Headers.CacheControl!.NoStore);
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return body.RootElement.Clone();
            }
            async Task<HttpResponseMessage> Put(WorkbookSessionDto editor, string currentLock, byte[] bytes)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/office/wopi/files/" + editor.SessionNId + "/contents") { Content = new ByteArrayContent(bytes) };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", editor.AccessToken);
                request.Headers.Add("X-WOPI-Override", "PUT"); request.Headers.Add("X-WOPI-Lock", currentLock);
                return await client.SendAsync(request);
            }
            profile.Office.PublicWopiBase = "https://wopi-gateway.example.invalid/";
            var info = await Info(first);
            Assert.Equal("https://studio.example.invalid", info.GetProperty("PostMessageOrigin").GetString());
            Assert.Equal("Book.xlsx", info.GetProperty("BaseFileName").GetString()); Assert.False(info.TryGetProperty("baseFileName", out _)); Assert.False(info.TryGetProperty("data", out _));
            Assert.Equal(JsonValueKind.Number, info.GetProperty("Size").ValueKind); Assert.Equal(original.Length, info.GetProperty("Size").GetInt64());
            Assert.Equal("1", info.GetProperty("Version").GetString()); Assert.True(info.GetProperty("UserCanWrite").GetBoolean());
            Assert.False((await Info(view)).GetProperty("UserCanWrite").GetBoolean());
            foreach (var (frozen, locked) in new[] { (true, false), (false, true), (false, false) })
            {
                await database.Client.Updateable<StudioWorkbook>().SetColumns(w => w.IsFrozen == frozen).SetColumns(w => w.IsLocked == locked).Where(w => w.NId == workbook.NId).ExecuteCommandAsync();
                Assert.Equal(!frozen && !locked, (await Info(first)).GetProperty("UserCanWrite").GetBoolean());
            }
            identity.Principal = p with { Permissions = p.Permissions.Where(permission => permission != "studio.workbook.edit").ToArray() };
            Assert.False((await Info(first)).GetProperty("UserCanWrite").GetBoolean()); identity.Principal = p;
            profile.Office.SupportsTokenRefresh = true;
            var renewed = await books.RenewAsync(p, workbook.NId, view.SessionNId, default);
            Assert.NotNull(renewed.AccessToken);
            Assert.Equal(401, (await Assert.ThrowsAsync<StudioException>(() => books.CheckFileInfoAsync(view.SessionNId, view.AccessToken, default))).StatusCode);
            view = view with { AccessToken = renewed.AccessToken, ExpiresOn = renewed.ExpiresOn };
            Assert.NotNull(await books.CheckFileInfoAsync(view.SessionNId, view.AccessToken, default));
            Assert.Equal("lock-a", await books.LockAsync(first.SessionNId, first.AccessToken, "LOCK", "lock-a", default));
            Assert.Equal("lock-rotated", await books.LockAsync(first.SessionNId, first.AccessToken, "LOCK", "lock-rotated", default, "lock-a"));
            await Assert.ThrowsAsync<WopiLockConflictException>(() => books.LockAsync(first.SessionNId, first.AccessToken, "LOCK", "wrong", default, "wrong-old"));
            Assert.Equal("lock-a", await books.LockAsync(first.SessionNId, first.AccessToken, "LOCK", "lock-a", default, "lock-rotated"));
            Assert.Equal("lock-a", (await Assert.ThrowsAsync<WopiLockConflictException>(() => books.LockAsync(stale.SessionNId, stale.AccessToken, "LOCK", "lock-b", default))).CurrentLock);
            Assert.Equal(403, (await Assert.ThrowsAsync<StudioException>(() => books.LockAsync(view.SessionNId, view.AccessToken, "LOCK", "lock-v", default))).StatusCode);
            using var changed = ReportPlanningTests.Package("custom/change.xml");
            using (var mismatch = await Put(first, "wrong-lock", changed.ToArray()))
            {
                Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode); Assert.Equal("lock-a", Assert.Single(mismatch.Headers.GetValues("X-WOPI-Lock")));
            }
            using (var pending = await Put(first, "lock-a", changed.ToArray()))
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, pending.StatusCode); Assert.Equal(TimeSpan.FromSeconds(2), pending.Headers.RetryAfter!.Delta);
                Assert.False(pending.Headers.Contains("X-WOPI-Lock")); Assert.Single(await books.VersionsAsync(p, workbook.NId, default));
            }
            await ScanPending();
            using (var saved = await Put(first, "lock-a", changed.ToArray()))
            {
                Assert.Equal(HttpStatusCode.OK, saved.StatusCode); Assert.Equal("2", Assert.Single(saved.Headers.GetValues("X-WOPI-ItemVersion")));
            }
            Assert.Equal(workbook.NId, (await books.ImportAsync(p, "Book", source.FileNId, "import-one", default)).NId);
            changed.Position = 0; Assert.Equal("2", (await books.PutFileAsync(first.SessionNId, first.AccessToken, "lock-a", changed, default)).ContentVersion);
            Assert.Single(await books.VersionsAsync(p, workbook.NId, default));
            Assert.True((await books.StateAsync(p, workbook.NId, first.SessionNId, default)).HasWorkingCopy);
            Assert.Equal("2", (await books.SaveVersionAsync(p, workbook.NId, "2", "Explicit checkpoint", default)).HistoryVersion);
            await books.LockAsync(first.SessionNId, first.AccessToken, "UNLOCK", "lock-a", default); await books.LockAsync(stale.SessionNId, stale.AccessToken, "LOCK", "lock-b", default);
            using var conflicted = ReportPlanningTests.Package("custom/recovery.xml");
            await Assert.ThrowsAsync<StudioException>(() => books.PutFileAsync(stale.SessionNId, stale.AccessToken, "lock-b", conflicted, default)); await ScanPending(); conflicted.Position = 0;
            var conflict = await books.PutFileAsync(stale.SessionNId, stale.AccessToken, "lock-b", conflicted, default); Assert.False(conflict.Saved); Assert.Equal("2", conflict.ContentVersion); Assert.NotNull(conflict.RecoveryFileNId);
            var versions = await books.VersionsAsync(p, workbook.NId, default); Assert.Equal(2, versions.Count);
            Assert.All(versions, version => Assert.Equal(TimeSpan.Zero, version.CreatedOn.Offset));
            await using var old = await books.ReadVersionAsync(p, workbook.NId, versions.Single(v => v.ContentVersion == "1").NId, default); using var oldBytes = new MemoryStream(); await old.CopyToAsync(oldBytes); Assert.Equal(original.ToArray(), oldBytes.ToArray());
            await books.LockAsync(stale.SessionNId, stale.AccessToken, "UNLOCK", "lock-b", default);
            await books.LockAsync(first.SessionNId, first.AccessToken, "LOCK", "lock-c", default);
            using var casContent = ReportPlanningTests.Package("custom/cas-recovery.xml");
            using (var pending = await Put(first, "lock-c", casContent.ToArray())) Assert.Equal(HttpStatusCode.ServiceUnavailable, pending.StatusCode);
            await ScanPending();
            // Force the database compare-and-swap to affect zero rows after insertion, exercising rollback/recovery rather than the stale-base precheck.
            await database.Client.Ado.ExecuteCommandAsync("CREATE TRIGGER workbook_reject_switch BEFORE UPDATE OF current_version_id ON studio_workbook BEGIN SELECT RAISE(IGNORE); END");
            try
            {
                using var rejected = await Put(first, "lock-c", casContent.ToArray()); Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
                using var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
                Assert.Equal("2", body.RootElement.GetProperty("currentContentVersion").GetString());
                var recoveryFileNId = body.RootElement.GetProperty("recoveryFileNId").GetString(); Assert.False(string.IsNullOrWhiteSpace(recoveryFileNId));
                Assert.Equal(2, (await books.VersionsAsync(p, workbook.NId, default)).Count);
                var owned = provider.GetRequiredService<IOwnedFileService>(); var context = new TrustedServiceContext(p.TenantNId, "platform-studio", p.SubjectNId);
                var references = await owned.ListByOwnerAsync(context, "WorkbookVersion", workbook.NId, null, default);
                var recovery = Assert.Single(references.Items, reference => reference.FileNId == recoveryFileNId);
                var ticket = await owned.GetReadTicketAsync(context, recovery.ReferenceNId, default);
                await using var recoveryContent = await owned.ReadAsync(context, new(recovery.ReferenceNId, p.SubjectNId, ticket.AuthorizationTicket), default);
                using var recovered = new MemoryStream(); await recoveryContent.CopyToAsync(recovered); Assert.Equal(casContent.ToArray(), recovered.ToArray());
            }
            finally { await database.Client.Ado.ExecuteCommandAsync("DROP TRIGGER workbook_reject_switch"); }
            // Change authorization/read-only state only after PutFile has passed its initial session check.
            // The same confirmed recovery bytes avoid conflating this with Pending scanning.
            foreach (var (frozen, locked) in new[] { (true, false), (false, true) })
            {
                using var interrupted = new ChangeDuringRead(casContent.ToArray(), async () =>
                {
                    await database.Client.Updateable<StudioWorkbook>().SetColumns(w => w.IsFrozen == frozen).SetColumns(w => w.IsLocked == locked).Where(w => w.NId == workbook.NId).ExecuteCommandAsync();
                });
                try
                {
                    var denied = await Assert.ThrowsAsync<StudioException>(() => books.PutFileAsync(first.SessionNId, first.AccessToken, "lock-c", interrupted, default));
                    Assert.Equal("WORKBOOK_READ_ONLY", denied.Code); Assert.Equal(403, denied.StatusCode);
                }
                finally
                {
                    await database.Client.Updateable<StudioWorkbook>().SetColumns(w => w.IsFrozen == false).SetColumns(w => w.IsLocked == false).Where(w => w.NId == workbook.NId).ExecuteCommandAsync();
                }
            }
            using (var revoked = new ChangeDuringRead(casContent.ToArray(), () =>
            {
                identity.Principal = p with { Permissions = p.Permissions.Where(permission => permission != "studio.workbook.edit").ToArray() };
                return Task.CompletedTask;
            }))
            {
                try { Assert.Equal(403, (await Assert.ThrowsAsync<StudioException>(() => books.PutFileAsync(first.SessionNId, first.AccessToken, "lock-c", revoked, default))).StatusCode); }
                finally { identity.Principal = p; }
            }
            Assert.Equal(2, (await books.VersionsAsync(p, workbook.NId, default)).Count);
            Assert.Equal("2", (await books.ListAsync(p, default)).Single(book => book.NId == workbook.NId).ContentVersion);
            await Assert.ThrowsAsync<StudioException>(() => books.CopyAsync(p, workbook.NId, "Independent", "2", "copy-one", default)); await ScanPending();
            var copy = await books.CopyAsync(p, workbook.NId, "Independent", "2", "copy-one", default); Assert.NotEqual(workbook.NId, copy.NId); Assert.Equal("1", copy.ContentVersion);
            var copyEditor = await books.CreateSessionAsync(p, copy.NId, "Edit", "1", new("https://office.invalid/browser/editor.html"), new("https://studio.invalid/"), default);
            await books.LockAsync(copyEditor.SessionNId, copyEditor.AccessToken, "LOCK", "copy-lock", default);
            async Task<WorkbookCommitResult> SaveWorking(WorkbookSessionDto editor, string currentLock, byte[] bytes)
            {
                using var firstAttempt = new MemoryStream(bytes);
                try { return await books.PutFileAsync(editor.SessionNId, editor.AccessToken, currentLock, firstAttempt, default); }
                catch (StudioException error) when (error.Code == "WORKBOOK_SCAN_PENDING")
                {
                    await ScanPending(); using var retry = new MemoryStream(bytes);
                    return await books.PutFileAsync(editor.SessionNId, editor.AccessToken, currentLock, retry, default);
                }
            }
            using var draftA = ReportPlanningTests.Package("custom/draft-a.xml");
            using var draftB = ReportPlanningTests.Package("custom/draft-b.xml");
            using var draftC = ReportPlanningTests.Package("custom/draft-c.xml");
            using var draftD = ReportPlanningTests.Package("custom/draft-d.xml");
            Assert.Equal("2", (await SaveWorking(copyEditor, "copy-lock", draftA.ToArray())).ContentVersion);
            var staleWorking = await books.CreateSessionAsync(p, copy.NId, "Edit", "2", new("https://office.invalid/browser/editor.html"), new("https://studio.invalid/"), default);
            var copyRow = await database.Client.Queryable<StudioWorkbook>().Where(w => w.NId == copy.NId).FirstAsync();
            var draftBefore = await database.Client.Queryable<StudioWorkbookVersion>().Where(v => v.WorkbookId == copyRow.Id && v.HistoryVersion == null).FirstAsync();
            Assert.Equal("3", (await SaveWorking(copyEditor, "copy-lock", draftB.ToArray())).ContentVersion);
            copyRow = await database.Client.Queryable<StudioWorkbook>().Where(w => w.NId == copy.NId).FirstAsync();
            var draftAfter = await database.Client.Queryable<StudioWorkbookVersion>().Where(v => v.WorkbookId == copyRow.Id && v.HistoryVersion == null).FirstAsync();
            Assert.Equal(draftBefore.Id, draftAfter.Id);
            Assert.Equal(2, await database.Client.Queryable<StudioWorkbookVersion>().Where(v => v.WorkbookId == copyRow.Id).CountAsync());
            Assert.Single(await books.VersionsAsync(p, copy.NId, default));
            await books.LockAsync(copyEditor.SessionNId, copyEditor.AccessToken, "UNLOCK", "copy-lock", default);
            await books.LockAsync(staleWorking.SessionNId, staleWorking.AccessToken, "LOCK", "stale-working", default);
            var staleWorkingSave = await SaveWorking(staleWorking, "stale-working", conflicted.ToArray());
            Assert.False(staleWorkingSave.Saved); Assert.NotNull(staleWorkingSave.RecoveryFileNId); Assert.Equal("3", staleWorkingSave.ContentVersion);
            await books.LockAsync(staleWorking.SessionNId, staleWorking.AccessToken, "UNLOCK", "stale-working", default);
            await books.LockAsync(copyEditor.SessionNId, copyEditor.AccessToken, "LOCK", "copy-lock", default);
            Assert.Equal("2", (await books.SaveVersionAsync(p, copy.NId, "3", "Pinned B", default)).HistoryVersion);
            Assert.Equal("2", (await books.SaveVersionAsync(p, copy.NId, "3", "Pinned B", default)).HistoryVersion);
            Assert.Equal(2, (await books.VersionsAsync(p, copy.NId, default)).Count);
            Assert.Equal("4", (await SaveWorking(copyEditor, "copy-lock", draftC.ToArray())).ContentVersion);
            Assert.Equal("5", (await SaveWorking(copyEditor, "copy-lock", draftD.ToArray())).ContentVersion);
            Assert.Equal(3, await database.Client.Queryable<StudioWorkbookVersion>().Where(v => v.WorkbookId == copyRow.Id).CountAsync());
            var pinnedB = (await books.VersionsAsync(p, copy.NId, default)).Single(v => v.ContentVersion == "2");
            await using (var pinnedContent = await books.ReadVersionAsync(p, copy.NId, pinnedB.NId, default))
            {
                using var actual = new MemoryStream(); await pinnedContent.CopyToAsync(actual); Assert.Equal(draftB.ToArray(), actual.ToArray());
            }
            copy = await books.StateAsync(p, copy.NId, copyEditor.SessionNId, default);
            var renamed = await books.EditAsync(p, copy.NId, "Renamed working copy", new(copy.OptimisticVersion, copy.ConcurrencyVersion), default);
            Assert.Equal("Renamed working copy", renamed.Name); Assert.Equal("2", renamed.HistoryVersion); Assert.Equal("5", renamed.ContentVersion);
            clock.Now = clock.Now.AddMinutes(2);
            Assert.Equal(2, await books.RecycleWorkingFilesAsync(default));
            Assert.Equal("Released", (await provider.GetRequiredService<IOwnedFileService>().GetStatusAsync(new(p.TenantNId, "platform-studio", p.SubjectNId), draftBefore.FileReferenceNId, default)).Status);
            Assert.Equal("DeletionRequested", (await files.GetFileAsync(p.TenantNId, draftBefore.FileNId, default))!.DeletionStatus);
            Assert.Equal(2, (await books.VersionsAsync(p, copy.NId, default)).Count);
            await books.CloseAsync(p, copy.NId, copyEditor.SessionNId, default);
            var latest = (await books.ListAsync(p, default)).Single(item => item.NId == workbook.NId); var archived = await books.ArchiveAsync(p, latest.NId, new(latest.OptimisticVersion, latest.ConcurrencyVersion), default); Assert.True(archived.IsArchived);
            Assert.False((await books.ListAsync(p, default)).Single(item => item.NId == copy.NId).IsArchived);
            Assert.Equal(403, (await Assert.ThrowsAsync<StudioException>(() => books.PutFileAsync(stale.SessionNId, stale.AccessToken, "lock-b", conflicted, default))).StatusCode);
            identity.Principal = p with { FactoryNIds = [] }; await Assert.ThrowsAsync<StudioException>(() => books.CheckFileInfoAsync(first.SessionNId, first.AccessToken, default));
            identity.Principal = p;
            profile.Office.SupportsTokenRefresh = false;
            var continuous = await books.CreateSessionAsync(p, copy.NId, "Edit", "5", new("https://office.invalid/browser/editor.html"), new("https://studio.invalid/"), default);
            Assert.True(continuous.SlidingSession);
            for (var i = 0; i < 3; i++)
            {
                clock.Now = clock.Now.AddMinutes(10);
                identity.Principal = p with { ExpiresOn = clock.Now.AddMinutes(2) };
                var lease = await books.RenewAsync(p, copy.NId, continuous.SessionNId, default);
                Assert.Null(lease.AccessToken);
                Assert.True(lease.ExpiresOn > clock.Now.AddMinutes(10));
                var persistedLease = await database.Client.Queryable<StudioWorkbookSession>().Where(s => s.NId == continuous.SessionNId).FirstUtcAsync(database.Provider, default);
                using var delegated = JsonDocument.Parse(persistedLease.IdentityEvidenceJson);
                Assert.Equal(lease.ExpiresOn, delegated.RootElement.GetProperty("principal").GetProperty("expiresOn").GetDateTimeOffset());
                Assert.NotNull(await books.CheckFileInfoAsync(continuous.SessionNId, continuous.AccessToken, default));
            }
            identity.Principal = p with { Permissions = p.Permissions.Where(permission => permission != "studio.workbook.edit").ToArray() };
            Assert.Equal(403, (await Assert.ThrowsAsync<StudioException>(() => books.RenewAsync(identity.Principal, copy.NId, continuous.SessionNId, default))).StatusCode);
            identity.Principal = p;
            await books.CloseAsync(p, copy.NId, continuous.SessionNId, default);
            Assert.Equal(401, (await Assert.ThrowsAsync<StudioException>(() => books.CheckFileInfoAsync(continuous.SessionNId, continuous.AccessToken, default))).StatusCode);
            await Assert.ThrowsAsync<StudioException>(() => books.RenewAsync(p, copy.NId, continuous.SessionNId, default));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}
