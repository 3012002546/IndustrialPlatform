using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Trace;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;
using IndustrialPlatform.PlatformStudio.Infrastructure.Integration.Embedded;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using IndustrialPlatform.PlatformStudio.Infrastructure.Trace;
using IndustrialPlatform.RuntimeSupport.Files;
using IndustrialPlatform.SystemData.Application.Files;
using IndustrialPlatform.SystemData.Application.Operations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class TracePreviewLifecycleTests
{
    private static readonly string Hash = Convert.ToHexStringLower(SHA256.HashData("proof"u8));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Imported_preview_stays_quarantined_until_clean_expires_in_30_minutes_and_cleans_up_safely(bool cleanBeforeExpiry)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var scope = fixture.Services.CreateScope();
        var owned = scope.ServiceProvider.GetRequiredService<IOwnedFileService>();
        var store = scope.ServiceProvider.GetRequiredService<IFileStore>();
        var files = scope.ServiceProvider.GetRequiredService<IFileService>();
        var mes = new StudioMesFiles(fixture.Profile, new ConfigurationBuilder().Build(), new HttpClient(new MesHandler()));
        var links = new Infrastructure.Trace.TraceNodeLinks(fixture.Profile, scope.ServiceProvider.GetRequiredService<IStudioOwnedFiles>(), mes, scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(), fixture.Clock, files, owned);
        var link = new JsonObject { ["kind"] = "File", ["openMode"] = "Modal", ["fileSource"] = new JsonObject { ["mode"] = "Mes", ["sourceKey"] = "mes" } };
        var input = new TraceLinkInput(link, JsonValue.Create("file-a"), new(), "v1");
        var context = new JsonObject { ["nodeKey"] = "server-node" };
        var pending = await Assert.ThrowsAsync<StudioException>(() => links.ResolveAsync(fixture.Principal, "model", link, input.Value, input.Parameters, "v1", context, default));
        Assert.Equal("STUDIO_FILE_SCAN_UNCONFIRMED", pending.Code);
        var fileNId = await fixture.Database.Client.Ado.GetStringAsync("SELECT file_n_id FROM system_file_object");
        var reference = await fixture.Database.Client.Ado.GetStringAsync("SELECT reference_n_id FROM file_owned_reference");
        var status = await owned.GetStatusAsync(fixture.Context, reference, default);
        Assert.Equal("Pending", status.Status);
        Assert.InRange((status.ExpiresOn!.Value - fixture.Clock.GetUtcNow()).TotalMinutes, 29.99, 30.01);
        if (cleanBeforeExpiry)
        {
            // Test scanner evidence only; production still requires the actual scanner worker.
            var raw = (await store.GetFileAsync("tenant", fileNId, default))!;
            await store.UpdateFileAsync(raw with { ScanStatus = "Clean" }, default);
            var result = await links.ResolveAsync(fixture.Principal, "model", link, input.Value, input.Parameters, "v1", context, default);
            var ticket = Uri.UnescapeDataString(result.ModalUrl.Split("ticket=")[1]);
            Assert.Equal(1, await fixture.Database.Client.Ado.GetIntAsync("SELECT COUNT(*) FROM file_owned_reference"));
            var content = await links.OpenAsync(fixture.Principal, "model", ticket, input, default);
            await using (content.Stream) Assert.Equal("proof", await new StreamReader(content.Stream).ReadToEndAsync());
            Assert.Equal("Active", (await owned.GetStatusAsync(fixture.Context, reference, default)).Status);
        }
        fixture.Clock.Now += TimeSpan.FromMinutes(31);
        Assert.Equal("FILE_PREVIEW_EXPIRED", (await Assert.ThrowsAsync<SystemDataOperationException>(() => owned.GetReadTicketAsync(fixture.Context, reference, default))).Code);
        Assert.Equal("FILE_PREVIEW_EXPIRED", (await Assert.ThrowsAsync<SystemDataOperationException>(() => owned.CommitAsync(fixture.Context, new(reference, fileNId, Hash), default))).Code);
        var cleanup = Assert.IsAssignableFrom<IOwnedFilePreviewCleanup>(owned);
        var sweep = await cleanup.CleanupExpiredPreviewsAsync(default);
        if (!cleanBeforeExpiry)
        {
            Assert.Equal(1, sweep.Retained); Assert.Equal(0, sweep.Released);
            var raw = (await store.GetFileAsync("tenant", fileNId, default))!;
            await store.UpdateFileAsync(raw with { ScanStatus = "Clean" }, default);
            sweep = await cleanup.CleanupExpiredPreviewsAsync(default);
        }
        Assert.Equal(1, sweep.Released);
        Assert.Equal("Released", (await owned.GetStatusAsync(fixture.Context, reference, default)).Status);
    }

    [Fact]
    public async Task Existing_asset_default_expiry_is_unchanged_and_preview_hold_prevents_cleanup()
    {
        await using var fixture = await Fixture.CreateAsync();
        var owned = fixture.Services.GetRequiredService<IOwnedFileService>();
        var asset = await owned.PrepareAsync(fixture.Context, new("asset", "platform-studio", "StudioAsset", "model", "proof.txt", "text/plain", 5, Hash), default);
        var status = await owned.GetStatusAsync(fixture.Context, asset.ReferenceNId, default);
        Assert.InRange((status.ExpiresOn!.Value - fixture.Clock.GetUtcNow()).TotalHours, 23.99, 24.01);
        var preview = await owned.PrepareAsync(fixture.Context, new("preview", "platform-studio", "DetailPreview", "detail", "proof.txt", "text/plain", 5, Hash), default);
        await owned.SetRetentionAsync(fixture.Context, new(preview.ReferenceNId, "0", null, true, "test hold"), default);
        fixture.Clock.Now += TimeSpan.FromMinutes(31);
        var result = await ((IOwnedFilePreviewCleanup)owned).CleanupExpiredPreviewsAsync(default);
        Assert.Equal(0, result.Released); Assert.Equal(1, result.Retained);
    }

    private sealed class MesHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("metadata", StringComparison.Ordinal) ? new JsonObject { ["externalFileId"] = "file-a", ["version"] = "v1", ["name"] = "proof.txt", ["contentType"] = "text/plain", ["length"] = "5", ["sha256"] = Hash, ["authorized"] = true }.ToJsonString() : "proof")
        });
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Identity(StudioPrincipal principal) : IStudioIdentity
    {
        public Task<StudioPrincipal> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(principal);
        public Task<StudioPrincipal> RefreshAsync(StudioPrincipal input, CancellationToken cancellationToken) => Task.FromResult(principal);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        public StudioPrincipal Principal { get; } = new("tenant", "user", "test", "test", [], ["studio.trace.run", "mes.file.read"], [], "1", "1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2));
        public TrustedServiceContext Context { get; } = new("tenant", "platform-studio", "user");
        public Clock Clock { get; } = new();
        public StudioDeploymentProfile Profile { get; }
        public StudioDatabase Database { get; }
        public ServiceProvider Services { get; }
        private Fixture(string root)
        {
            _root = root; Profile = StudioInitializationTests.Profile(Path.Combine(root, "studio.db"));
            Profile.Files = new() { Provider = "Embedded", Root = Path.Combine(root, "files") };
            Profile.MesFileSources["mes"] = new() { MetadataEndpointKey = "files", ContentEndpointKey = "files", PermissionAction = "mes.file.read", ScanPolicy = "ImportAndScan", AllowedContentTypes = ["text/plain"] };
            Profile.SourceRegistry["files"] = new() { BaseAddress = "https://mes.test/api/", ReadOnlyVerified = true, Operations = new() { ["metadata"] = new() { Path = "metadata" }, ["content"] = new() { Path = "content" } } };
            var configuration = new ConfigurationBuilder().Build(); Database = new(Profile, configuration);
            var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<IConfiguration>(configuration); services.AddSingleton<TimeProvider>(Clock); services.AddSingleton(Profile); services.AddSingleton(Database); services.AddSingleton<IStudioIdentity>(new Identity(Principal));
            services.AddScoped<IStudioResourceStore, StudioResourceStore>(); services.AddScoped<IStudioAuthorization, StudioAuthorization>();
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(root, "keys"))); services.AddStudioEmbeddedFileAudit(Profile); Services = services.BuildServiceProvider();
        }
        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "trace-file-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var fixture = new Fixture(root); await new StudioServiceInitializer(fixture.Database, fixture.Profile, fixture.Clock).InitializeLocalAsync(default); return fixture;
        }
        public async ValueTask DisposeAsync() { await Services.DisposeAsync(); Database.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(_root, true); }
    }
}
