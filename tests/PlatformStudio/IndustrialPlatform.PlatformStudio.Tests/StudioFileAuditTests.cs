using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;
using IndustrialPlatform.PlatformStudio.Infrastructure.Integration.Embedded;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using IndustrialPlatform.RuntimeSupport.Files;
using IndustrialPlatform.RuntimeSupport.Persistence;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.Files;
using IndustrialPlatform.SystemData.Application.Operations;
using IndustrialPlatform.SystemData.Contracts.Files;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using IndustrialPlatform.PlatformStudio.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using System.Net;
using System.Net.Http.Json;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class StudioFileAuditTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly string[] LedgerReaders = ["user-b", "user-c"];
    [Fact]
    public async Task Owned_sqlite_upload_scan_retention_ticket_release_and_delete_are_persistent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var owned = fixture.Services.GetRequiredService<IOwnedFileService>();
        var files = fixture.Services.GetRequiredService<IFileService>();
        var store = fixture.Services.GetRequiredService<IFileStore>();
        var request = new OwnedFilePrepareV1("operation-a", "platform-studio", "StudioAsset", "dashboard-a", "proof.txt", "text/plain", 5, Hash("proof"));
        var prepared = await owned.PrepareAsync(fixture.Context, request, Ct);
        Assert.Equal(prepared, await owned.PrepareAsync(fixture.Context, request, Ct));
        await Assert.ThrowsAsync<SystemDataOperationException>(() => owned.PrepareAsync(fixture.Context, request with { OwnerNId = "different" }, Ct));
        var file = await UploadAsync(files, fixture.Context, prepared.UploadSessionNId, "proof");
        Assert.True(await store.HasActiveReferencesAsync("tenant-a", file.FileNId, Ct));
        Assert.False(await store.TryRequestDeletionAsync((await store.GetFileAsync("tenant-a", file.FileNId, Ct))! with { DeletionStatus = "DeletionRequested" }, Ct));
        Assert.Equal("Quarantined", (await owned.CommitAsync(fixture.Context, new(prepared.ReferenceNId, file.FileNId, Hash("proof")), Ct)).Status);
        await Assert.ThrowsAsync<SystemDataOperationException>(() => owned.GetReadTicketAsync(fixture.Context, prepared.ReferenceNId, Ct));
        var raw = (await store.GetFileAsync("tenant-a", file.FileNId, Ct))!;
        await store.UpdateFileAsync(raw with { ScanStatus = "Clean" }, Ct); // Test scanner evidence; no production bypass.
        var active = await owned.CommitAsync(fixture.Context, new(prepared.ReferenceNId, file.FileNId, Hash("proof")), Ct);
        Assert.Equal("Active", active.Status); Assert.Equal("1", active.Version);
        Assert.Equal(active, await owned.CommitAsync(fixture.Context, new(prepared.ReferenceNId, file.FileNId, Hash("proof")), Ct));
        Assert.Single((await owned.ListByOwnerAsync(fixture.Context, "StudioAsset", "dashboard-a", null, Ct)).Items);
        Assert.Empty((await owned.ListByOwnerAsync(fixture.Context, "StudioAsset", "dashboard-a", prepared.ReferenceNId, Ct)).Items);
        var ticket = await owned.GetReadTicketAsync(fixture.Context, prepared.ReferenceNId, Ct);
        await using (var stream = await owned.ReadAsync(fixture.Context, new(prepared.ReferenceNId, "user-a", ticket.AuthorizationTicket), Ct)) Assert.Equal("proof", await new StreamReader(stream).ReadToEndAsync(Ct));
        await Assert.ThrowsAsync<SystemDataOperationException>(() => owned.ReadAsync(fixture.Context with { ActorUserNId = "user-b" }, new(prepared.ReferenceNId, "user-b", ticket.AuthorizationTicket), Ct));
        fixture.Clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal("FILE_READ_TICKET_INVALID", (await Assert.ThrowsAsync<SystemDataOperationException>(() => owned.ReadAsync(fixture.Context, new(prepared.ReferenceNId, "user-a", ticket.AuthorizationTicket), Ct))).Code);
        await Assert.ThrowsAsync<SystemDataOperationException>(() => owned.GetStatusAsync(fixture.Context with { TenantNId = "tenant-b" }, prepared.ReferenceNId, Ct));
        var hold = await owned.SetRetentionAsync(fixture.Context, new(prepared.ReferenceNId, active.Version, DateTimeOffset.UtcNow.AddDays(1), true, "retention"), Ct);
        await Assert.ThrowsAsync<SystemDataOperationException>(() => owned.ReleaseAsync(fixture.Context, new(prepared.ReferenceNId, hold.Version, "cleanup"), Ct));
        await Assert.ThrowsAsync<SystemDataOperationException>(() => owned.SetRetentionAsync(fixture.Context, new(prepared.ReferenceNId, "1", null, false, null), Ct));
        var clear = await owned.SetRetentionAsync(fixture.Context, new(prepared.ReferenceNId, hold.Version, null, false, null), Ct);
        var released = await owned.ReleaseAsync(fixture.Context, new(prepared.ReferenceNId, clear.Version, "cleanup"), Ct);
        Assert.Equal("Released", released.Status);
        await Assert.ThrowsAsync<SystemDataOperationException>(() => owned.ReadAsync(fixture.Context, new(prepared.ReferenceNId, "user-a", ticket.AuthorizationTicket), Ct));
        Assert.False(await store.HasActiveReferencesAsync("tenant-a", file.FileNId, Ct));
        Assert.True(await store.TryRequestDeletionAsync(raw with { DeletionStatus = "DeletionRequested" }, Ct));
        Assert.True(await fixture.Database.Client.Ado.GetIntAsync("SELECT COUNT(*) FROM system_audit_fact") > 0);
        var dispatcher = new StudioFileStatusDispatcher(fixture.Services.GetRequiredService<IServiceScopeFactory>(), fixture.Clock, NullLogger<StudioFileStatusDispatcher>.Instance);
        await dispatcher.DispatchOnceAsync(Ct);
        Assert.Equal(0, await fixture.Database.Client.Ado.GetIntAsync("SELECT COUNT(*) FROM system_file_status_outbox WHERE published_on IS NULL"));
        Assert.True(await fixture.Database.Client.Ado.GetIntAsync("SELECT COUNT(*) FROM system_audit_fact WHERE action='file.status.observed'") > 0);
        Assert.False(await fixture.Database.TableExistsAsync("system_collaboration_file_hold", Ct));
    }

    [Fact]
    public async Task File_and_audit_share_transaction_and_audit_failure_rolls_back_file_session()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Database.Client.Ado.ExecuteCommandAsync("DROP TABLE system_audit_lifecycle");
        var files = fixture.Services.GetRequiredService<IFileService>();
        await Assert.ThrowsAnyAsync<Exception>(() => files.CreateSessionAsync("tenant-a", "user-a", new() { SessionNId = "rollback", FileName = "x", Length = 0 }, Ct));
        Assert.Equal(0, await fixture.Database.Client.Ado.GetIntAsync("SELECT COUNT(*) FROM system_file_upload_session"));
        Assert.Equal(0, await fixture.Database.Client.Ado.GetIntAsync("SELECT COUNT(*) FROM system_audit_fact"));
    }

    [Fact]
    public async Task Transfer_rechecks_scope_and_personal_references_do_not_expand_to_another_user()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var scope = fixture.Services.CreateScope();
        var transfer = scope.ServiceProvider.GetRequiredService<IStudioFileTransfer>();
        var request = new CreateUploadSessionRequest { FileName = "proof.txt", ContentType = "text/plain", Length = 5, Sha256 = Hash("proof"), Purpose = "browser-forged" };
        var session = await transfer.CreateScopedSessionAsync(fixture.Context, request, new("LowCode", null, "TemplatePackage"), Ct);
        Assert.Equal("studio.LowCode.TemplatePackage", session.Purpose);
        var persisted = await fixture.Services.GetRequiredService<IFileStore>().GetSessionAsync("tenant-a", session.SessionNId, Ct);
        Assert.Contains("platform-studio", persisted!.OwnerContextJson);
        fixture.Identity.Principal = fixture.Identity.Principal with { Permissions = [] };
        await Assert.ThrowsAsync<StudioException>(() => transfer.GetSessionAsync(fixture.Context, session.SessionNId, Ct));
        fixture.Identity.Principal = Fixture.Principal;
        await Assert.ThrowsAsync<SystemDataOperationException>(() => transfer.GetSessionAsync(fixture.Context with { ActorUserNId = "user-b" }, session.SessionNId, Ct));
        var file = await UploadAsync(fixture.Services.GetRequiredService<IFileService>(), fixture.Context, session.SessionNId, "proof");
        var store = fixture.Services.GetRequiredService<IFileStore>();
        await store.UpdateFileAsync((await store.GetFileAsync("tenant-a", file.FileNId, Ct))! with { ScanStatus = "Clean" }, Ct);
        await transfer.AddReferenceAsync(fixture.Context, file.FileNId, "personal-a", new() { Purpose = "ledger" }, Ct);
        var adapter = scope.ServiceProvider.GetRequiredService<IStudioFiles>();
        await adapter.ValidateReferenceAsync(Fixture.Principal, file.FileNId, "personal-a", Ct);
        fixture.Identity.Principal = Fixture.Principal with { SubjectNId = "user-b" };
        await Assert.ThrowsAsync<StudioException>(() => adapter.ValidateReferenceAsync(fixture.Identity.Principal, file.FileNId, "personal-a", Ct));
    }

    [Fact]
    public async Task Ledger_readers_can_use_existing_personal_references_without_upload_write_permission()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var scope = fixture.Services.CreateScope();
        var resourceStore = scope.ServiceProvider.GetRequiredService<IStudioResourceStore>();
        var authorization = scope.ServiceProvider.GetRequiredService<IStudioAuthorization>();
        var profile = fixture.Services.GetRequiredService<StudioDeploymentProfile>();
        var publishing = new IndustrialPlatform.PlatformStudio.Application.Publishing.StudioPublishing(resourceStore, authorization, new(resourceStore, fixture.Clock), profile, fixture.Clock, [], new StudioTestSourceExecutor());
        var model = StudioPublishingTests.Model();
        var created = await publishing.CreateAsync(Fixture.Principal, new("read-model", "LedgerModel", "read model", null, null, model), "create-read-model", Ct);
        var plan = await publishing.ValidateAsync(Fixture.Principal, created.NId, model, Ct);
        await publishing.PublishAsync(Fixture.Principal, created.NId, new(created.OptimisticVersion, created.ConcurrencyVersion, "reader test", true, plan.DependencyPlanHash), "publish-read-model", Ct);
        var resource = (await resourceStore.FindResourceAsync("tenant-a", created.NId, Ct))!;
        var ownerGrants = await resourceStore.GetGrantsAsync("tenant-a", resource.Id, Ct);
        await resourceStore.ReplaceGrantsAsync("tenant-a", resource.Id, ownerGrants.Concat(LedgerReaders.Select(user => new StudioGrant { NId = "grant-" + user, TenantNId = "tenant-a", ResourceId = resource.Id, SubjectNId = user, Capability = "Run", GrantedBy = "user-a", CreatedOn = fixture.Clock.GetUtcNow() })).ToArray(), Ct);
        var transfer = scope.ServiceProvider.GetRequiredService<IStudioFileTransfer>();
        var session = await transfer.CreateScopedSessionAsync(fixture.Context, new() { FileName = "proof.txt", ContentType = "text/plain", Length = 5, Sha256 = Hash("proof") }, new("LowCode", created.NId, "LedgerField"), Ct);
        var file = await UploadAsync(fixture.Services.GetRequiredService<IFileService>(), fixture.Context, session.SessionNId, "proof");
        fixture.Identity.Principal = Fixture.Principal with { Permissions = ["studio.ledger.read"] };
        // The uploader can poll quarantined metadata after write permission is removed; content remains blocked.
        Assert.Equal(file.FileNId, (await transfer.GetFileAsync(fixture.Context, file.FileNId, Ct)).FileNId);
        Assert.Equal("FILE_SCAN_PENDING", (await Assert.ThrowsAsync<SystemDataOperationException>(() => transfer.OpenContentAsync(fixture.Context, file.FileNId, null, Ct))).Code);
        Assert.Equal(403, (await Assert.ThrowsAsync<StudioException>(() => transfer.PauseAsync(fixture.Context, session.SessionNId, Ct))).StatusCode);
        var store = fixture.Services.GetRequiredService<IFileStore>();
        await store.UpdateFileAsync((await store.GetFileAsync("tenant-a", file.FileNId, Ct))! with { ScanStatus = "Clean" }, Ct);
        await store.InsertReferenceAsync(new("tenant-a", "reader-personal-reference", file.FileNId, "user-b", "ledger", fixture.Clock.GetUtcNow(), null), Ct);
        fixture.Identity.Principal = Fixture.Principal with { SubjectNId = "user-b", Permissions = ["studio.ledger.read"] };
        var reader = fixture.Context with { ActorUserNId = "user-b" };
        Assert.Equal(file.FileNId, (await transfer.GetFileAsync(reader, file.FileNId, Ct)).FileNId);
        await using (var content = await transfer.OpenContentAsync(reader, file.FileNId, "reader-personal-reference", Ct)) Assert.Equal("proof", await new StreamReader(content).ReadToEndAsync(Ct));
        fixture.Identity.Principal = fixture.Identity.Principal with { SubjectNId = "user-c" };
        await Assert.ThrowsAsync<SystemDataOperationException>(() => transfer.GetFileAsync(reader with { ActorUserNId = "user-c" }, file.FileNId, Ct));
        fixture.Identity.Principal = fixture.Identity.Principal with { SubjectNId = "user-b" };
        await store.DeleteReferenceAsync("tenant-a", "reader-personal-reference", fixture.Clock.GetUtcNow(), Ct);
        await Assert.ThrowsAsync<SystemDataOperationException>(() => transfer.GetFileAsync(reader, file.FileNId, Ct));
    }

    [Fact]
    public async Task Audit_outbox_delivers_durably_and_duplicate_event_hash_conflicts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var store = new StudioResourceStore(fixture.Database, fixture.Clock);
        await store.AppendAuditAsync(Fixture.Principal, "studio.test", "resource-a", new JsonObject { ["version"] = "1" }, Ct);
        var item = Assert.Single(await store.PendingAuditsAsync(10, Ct));
        using var scope = fixture.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<IStudioAudit>();
        await audit.IngestAsync(item, Ct); await audit.IngestAsync(item, Ct);
        Assert.Equal(1, await fixture.Database.Client.Ado.GetIntAsync("SELECT COUNT(*) FROM system_audit_fact"));
        var payload = JsonNode.Parse(item.PayloadJson)!.AsObject(); payload["payloadJson"] = "{\"version\":\"2\"}"; item.PayloadJson = payload.ToJsonString();
        var conflict = await Assert.ThrowsAsync<SystemDataOperationException>(() => audit.IngestAsync(item, Ct));
        Assert.Equal("AUDIT_FACT_CONFLICT", conflict.Code);
        Assert.Single(await scope.ServiceProvider.GetRequiredService<IStudioResourceStore>().PendingAuditsAsync(10, Ct));
        var dispatcher = new StudioAuditDispatcher(fixture.Services.GetRequiredService<IServiceScopeFactory>(), fixture.Clock, NullLogger<StudioAuditDispatcher>.Instance);
        await dispatcher.DispatchOnceAsync(Ct);
        Assert.Empty(await store.PendingAuditsAsync(10, Ct));
        Assert.Equal("Sent", await fixture.Database.Client.Ado.GetStringAsync("SELECT status FROM studio_audit_outbox"));
        Assert.Equal(0, await fixture.Database.Client.Ado.GetIntAsync("SELECT attempt_count FROM studio_audit_outbox"));
    }

    [Fact]
    public async Task External_audit_http_filters_in_database_scopes_tenant_and_enforces_view_export_permissions()
    {
        await using var fixture = await Fixture.CreateAsync();
        var audit = fixture.Services.GetRequiredService<IAuditService>();
        foreach (var (tenant, actor, eventNId) in new[] { ("tenant-a", "user-a", "match"), ("tenant-a", "user-b", "other-actor"), ("tenant-b", "user-a", "other-tenant") })
            await audit.IngestAsync(tenant, new() { ProducerServiceKey = "platform-studio", AuditEventNId = eventNId, OccurredOn = fixture.Clock.GetUtcNow().AddSeconds(-1), ActorUserNId = actor, Action = "audit.fixture", ObjectType = "StudioResource", ObjectNId = "object-a", PayloadJson = "{}" }, Ct);
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(fixture.Services.GetRequiredService<IAuditFilteredQueries>());
        builder.Services.AddSingleton<IStudioAuthorization>(new StudioAuthorization(new StudioResourceStore(fixture.Database, fixture.Clock), fixture.Clock));
        await using var app = builder.Build();
        string[] permissions = ["studio.audit.view", "studio.audit.export"];
        var group = app.MapGroup("/api/v1/studio");
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Items["StudioPrincipal"] = Fixture.Principal with { Permissions = permissions };
            try { return await next(context); }
            catch (StudioException exception) { return Results.Json(new { code = exception.Code }, statusCode: exception.StatusCode); }
            catch (SystemDataOperationException exception) { return Results.Json(new { code = exception.Code }, statusCode: exception.StatusCode); }
        });
        group.MapStudioAudit(fixture.Services.GetRequiredService<StudioDeploymentProfile>());
        await app.StartAsync(Ct);
        using var client = app.GetTestClient();
        var response = await client.GetAsync("/api/v1/studio/audit?action=audit.fixture&actor=user-a&objectNId=object-a&tenantNId=tenant-b", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
        Assert.Equal("1", body["data"]!["total"]!.GetValue<string>());
        Assert.Equal("match", body["data"]!["items"]![0]!["auditEventNId"]!.GetValue<string>());
        response = await client.GetAsync("/api/v1/studio/audit?action=audit.fixture&actor=" + Uri.EscapeDataString("user-a' OR 1=1 --"), Ct);
        body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
        Assert.Equal("0", body["data"]!["total"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/studio/audit?pageSize=201", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/studio/audit?page=2147483647", Ct)).StatusCode);
        permissions = ["studio.audit.view"];
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/studio/audit/export", new { action = "audit.fixture" }, Ct)).StatusCode);
        permissions = ["studio.audit.export"];
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/studio/audit", Ct)).StatusCode);
        response = await client.PostAsJsonAsync("/api/v1/studio/audit/export", new { action = "audit.fixture", actor = "user-a", objectNId = "object-a" }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        var csv = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("match", csv, StringComparison.Ordinal); Assert.DoesNotContain("other-actor", csv, StringComparison.Ordinal); Assert.DoesNotContain("other-tenant", csv, StringComparison.Ordinal);
        using var scope = fixture.Services.CreateScope();
        var capability = await scope.ServiceProvider.GetRequiredService<StudioFileCapabilityProbe>().GetStatusAsync(Ct);
        Assert.False(capability.Available); Assert.Equal("Unknown", capability.Status); Assert.Equal("STUDIO_SCANNER_UNAVAILABLE", capability.ReasonCode);
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static async Task<FileObjectV1> UploadAsync(IFileService files, TrustedServiceContext context, string sessionNId, string text)
    {
        await files.SetContentHashAsync(context.TenantNId, sessionNId, context.ActorUserNId, new() { Sha256 = Hash(text) }, Ct);
        var session = await files.ResumeProofAsync(context.TenantNId, sessionNId, context.ActorUserNId, new() { WriterEpoch = 1, Proof = Hash(text) }, Ct);
        await files.AppendAsync(context.TenantNId, session.TransportId, context.ActorUserNId, 0, 1, new MemoryStream(Encoding.UTF8.GetBytes(text)), session.ResumeTicket, Ct);
        return await files.CompleteAsync(context.TenantNId, sessionNId, context.ActorUserNId, Ct);
    }
    private sealed class Identity : IStudioIdentity
    {
        public StudioPrincipal Principal { get; set; } = Fixture.Principal;
        public Task<StudioPrincipal> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(Principal);
        public Task<StudioPrincipal> RefreshAsync(StudioPrincipal principal, CancellationToken cancellationToken) => Task.FromResult(Principal);
    }
    private sealed class AdjustableClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        public static StudioPrincipal Principal => new("tenant-a", "user-a", "test", "test", [], StudioKinds.Permissions, [], "1", "1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
        public TrustedServiceContext Context { get; } = new("tenant-a", "platform-studio", "user-a");
        public StudioDatabase Database { get; }
        public ServiceProvider Services { get; }
        public Identity Identity { get; } = new();
        public AdjustableClock Clock { get; } = new();
        private Fixture(string root)
        {
            _root = root;
            var profile = StudioInitializationTests.Profile(Path.Combine(root, "studio.db")); profile.Files = new() { Provider = "Embedded", Root = Path.Combine(root, "files") };
            Database = new(profile, new ConfigurationBuilder().Build());
            var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton(profile); services.AddSingleton(Database); services.AddSingleton<IStudioIdentity>(Identity);
            services.AddScoped<IStudioResourceStore, StudioResourceStore>(); services.AddScoped<IStudioAuthorization, StudioAuthorization>();
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(root, "keys")));
            services.AddStudioEmbeddedFileAudit(profile); Services = services.BuildServiceProvider();
        }
        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "studio-file-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var fixture = new Fixture(root);
            await new StudioServiceInitializer(fixture.Database, fixture.Services.GetRequiredService<StudioDeploymentProfile>(), TimeProvider.System).InitializeLocalAsync(Ct);
            return fixture;
        }
        public async ValueTask DisposeAsync() { await Services.DisposeAsync(); Database.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(_root, true); }
    }
}
