using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Trace;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Integration.Embedded;
using IndustrialPlatform.PlatformStudio.Infrastructure.Trace;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class TraceNodeLinkTests
{
    private static StudioPrincipal Principal => new("tenant", "user", "test", "test", [], ["studio.trace.run", "mes.file.read"], ["factory"], "1", "1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
    private static readonly string Hash = Convert.ToHexStringLower(SHA256.HashData("proof"u8));
    private static JsonObject FileLink => new() { ["kind"] = "File", ["openMode"] = "Modal", ["fileSource"] = new JsonObject { ["mode"] = "Mes", ["sourceKey"] = "mes" } };
    private static StudioDeploymentProfile Profile(string scan = "VerifySourceEvidence") => new()
    {
        MesFileSources = new() { ["mes"] = new() { MetadataEndpointKey = "files", ContentEndpointKey = "files", PermissionAction = "mes.file.read", ScanPolicy = scan, TrustedScannerKey = "scanner", AllowedContentTypes = ["text/plain"] } },
        SourceRegistry = new() { ["files"] = new() { BaseAddress = "https://mes.test/api/", ReadOnlyVerified = true, CredentialRef = "mesSecret", Operations = new() { ["metadata"] = new() { Path = "metadata" }, ["content"] = new() { Path = "content" } } } },
        TraceExternalLinks = new() { ["erp"] = new() { BaseAddress = "https://erp.test/document", AllowedParameters = ["lot"], Embeddable = false } }
    };
    private static JsonObject Metadata(string status = "Clean", string hash = "", string version = "v1") => new()
    {
        ["externalFileId"] = "file-a", ["version"] = version, ["name"] = "proof.txt", ["contentType"] = "text/plain", ["length"] = "5", ["sha256"] = hash.Length == 0 ? Hash : hash, ["authorized"] = true,
        ["scanEvidence"] = new JsonObject { ["scannerKey"] = "scanner", ["status"] = status, ["version"] = version, ["sha256"] = hash.Length == 0 ? Hash : hash, ["scannedOn"] = DateTimeOffset.UtcNow.ToString("O") }
    };
    private static StudioMesFiles Mes(StudioDeploymentProfile profile, Handler handler) => new(profile, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["mesSecret"] = "server-secret" }).Build(), new HttpClient(handler));
    private static Infrastructure.Trace.TraceNodeLinks Links(StudioDeploymentProfile profile, StudioMesFiles mes, TimeProvider? clock = null) => new(profile, DispatchProxy.Create<IStudioOwnedFiles, UnexpectedCall>(), mes, new EphemeralDataProtectionProvider(), clock ?? TimeProvider.System);

    [Fact]
    public async Task External_uses_registered_base_and_encoded_values_with_explicit_browser_fallback()
    {
        var profile = Profile(); var handler = new Handler(); var links = Links(profile, Mes(profile, handler));
        var result = await links.ResolveAsync(Principal, "model", new() { ["kind"] = "External", ["openMode"] = "Modal", ["allowedOriginKey"] = "erp" }, JsonValue.Create("https://evil.test/?a=1&b=2"), new() { ["lot"] = "A&B" }, null, new(), default);
        Assert.StartsWith("https://erp.test/document?value=https%3A%2F%2Fevil.test", result.ModalUrl);
        Assert.Contains("lot=A%26B", result.ModalUrl); Assert.False(result.Embeddable); Assert.Equal(0, handler.Calls);
        await Assert.ThrowsAsync<StudioException>(() => links.ResolveAsync(Principal, "model", new() { ["kind"] = "External", ["openMode"] = "NewTab", ["allowedOriginKey"] = "erp" }, JsonValue.Create("x"), new(), null, new(), default));
        await Assert.ThrowsAsync<StudioException>(() => links.ResolveAsync(Principal, "model", new() { ["kind"] = "External", ["openMode"] = "Modal", ["allowedOriginKey"] = "missing" }, JsonValue.Create("x"), new(), null, new(), default));
    }

    [Fact]
    public async Task Mes_ticket_binds_identity_input_and_expiry_and_never_contains_credentials()
    {
        var profile = Profile(); var handler = new Handler(); var clock = new Clock(); var links = Links(profile, Mes(profile, handler), clock);
        var link = FileLink; var value = JsonValue.Create("file-a"); var input = new TraceLinkInput(link, value, new(), "v1");
        var result = await links.ResolveAsync(Principal, "model", link, value, new(), "v1", new() { ["nodeKey"] = "node", ["revisionNId"] = "revision" }, default);
        var ticket = Uri.UnescapeDataString(result.ModalUrl.Split("ticket=")[1]);
        Assert.True(result.ModalUrl.Length < 1024);
        Assert.DoesNotContain("server-secret", result.ModalUrl); Assert.Equal("node", links.ReadTicketContext(Principal, "model", ticket)["nodeKey"]!.GetValue<string>());
        Assert.Throws<StudioException>(() => links.ReadTicketContext(Principal with { SubjectNId = "other" }, "model", ticket));
        await Assert.ThrowsAsync<StudioException>(() => links.OpenAsync(Principal, "model", ticket, input with { Value = JsonValue.Create("other-file") }, default));
        var content = await links.OpenAsync(Principal, "model", ticket, input, default);
        await using (content.Stream) Assert.Equal("proof", await new StreamReader(content.Stream).ReadToEndAsync());
        Assert.NotNull(result.NewTabUrl);
        Assert.StartsWith("/screen/trace-link?nId=model&ticket=", result.NewTabUrl);
        Assert.DoesNotContain("server-secret", result.NewTabUrl);
        // A standalone browser page may refresh independently of the original modal.
        clock.Now += TimeSpan.FromSeconds(31);
        var refreshed = await links.OpenAsync(Principal, "model", ticket, input, default);
        await using (refreshed.Stream) Assert.Equal("proof", await new StreamReader(refreshed.Stream).ReadToEndAsync());
        Assert.Throws<StudioException>(() => links.ReadTicketContext(Principal with { AuthVersion = "2" }, "model", ticket));
        Assert.Throws<StudioException>(() => links.ReadTicketContext(Principal with { PolicyVersion = "2" }, "model", ticket));
        clock.Now += TimeSpan.FromMinutes(5);
        Assert.Throws<StudioException>(() => links.ReadTicketContext(Principal, "model", ticket));
    }

    [Fact]
    public async Task Preview_url_preserves_path_base_and_expires_with_the_current_identity()
    {
        var profile = Profile(); profile.PathBase = "/studio";
        var handler = new Handler(); var clock = new Clock(); var links = Links(profile, Mes(profile, handler), clock);
        var principal = Principal with { ExpiresOn = clock.Now.AddSeconds(20) };
        var link = FileLink; link["label"] = "Batch report";
        var result = await links.ResolveAsync(principal, "model", link, JsonValue.Create("file-a"), new(), "v1", new(), default);
        Assert.Equal(principal.ExpiresOn, result.ExpiresOn);
        Assert.Equal("Batch report", result.Title);
        Assert.StartsWith("/studio/screen/trace-link?", result.NewTabUrl);
        Assert.StartsWith("/studio/api/v1/studio/trace/model/link-content?", result.ModalUrl);
    }

    [Fact]
    public async Task Runtime_file_preview_requires_execution_permission_not_model_management_visibility()
    {
        var profile = Profile(); var links = Links(profile, Mes(profile, new Handler()));
        var result = await links.ResolveAsync(Principal, "model", FileLink, JsonValue.Create("file-a"), new(), "v1", new(), default);
        Assert.Equal("File", result.Kind);
        var denied = await Assert.ThrowsAsync<StudioException>(() => links.ResolveAsync(Principal with { Permissions = ["studio.trace.view", "mes.file.read"] }, "model", FileLink, JsonValue.Create("file-a"), new(), "v1", new(), default));
        Assert.Equal("STUDIO_FORBIDDEN", denied.Code);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("Pending")]
    [InlineData("Rejected")]
    public async Task Mes_rejects_non_clean_source_evidence(string status)
    {
        var profile = Profile(); var handler = new Handler { Document = Metadata(status) };
        var error = await Assert.ThrowsAsync<StudioException>(() => Mes(profile, handler).GetMetadataAsync(Principal, "mes", "file-a", "v1", default));
        Assert.Equal("STUDIO_MES_SCAN_UNCONFIRMED", error.Code);
    }

    [Fact]
    public async Task Mes_rejects_changed_content_and_permission_revocation_and_redirects()
    {
        var profile = Profile(); var handler = new Handler { Body = "wrong" }; var mes = Mes(profile, handler);
        var metadata = await mes.GetMetadataAsync(Principal, "mes", "file-a", "v1", default);
        Assert.Equal("STUDIO_MES_FILE_CHANGED", (await Assert.ThrowsAsync<StudioException>(() => mes.OpenAsync(Principal, "mes", metadata, default))).Code);
        var calls = handler.Calls;
        await Assert.ThrowsAsync<StudioException>(() => mes.GetMetadataAsync(Principal with { Permissions = ["studio.trace.view"] }, "mes", "file-a", "v1", default));
        Assert.Equal(calls, handler.Calls);
        handler.Redirect = true;
        await Assert.ThrowsAsync<StudioException>(() => mes.GetMetadataAsync(Principal, "mes", "file-a", "v1", default));
        handler.Redirect = false; handler.Document["authorized"] = false;
        await Assert.ThrowsAsync<StudioException>(() => mes.GetMetadataAsync(Principal, "mes", "file-a", "v1", default));
    }

    [Fact]
    public async Task Import_and_scan_requires_actual_file_adapter_instead_of_trusting_source_clean_flag()
    {
        var profile = Profile("ImportAndScan"); profile.Files.Provider = "Disabled";
        var handler = new Handler { Document = Metadata("Unknown") };
        var links = Links(profile, Mes(profile, handler));
        var error = await Assert.ThrowsAsync<StudioException>(() => links.ResolveAsync(Principal, "model", FileLink, JsonValue.Create("file-a"), new(), "v1", new(), default));
        Assert.Equal("STUDIO_FILES_UNAVAILABLE", error.Code);
    }

    public class UnexpectedCall : DispatchProxy { protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new InvalidOperationException(targetMethod?.Name); }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Handler : HttpMessageHandler
    {
        public JsonObject Document = Metadata(); public string Body = "proof"; public bool Redirect; public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Assert.Equal("https", request.RequestUri!.Scheme); Assert.Equal("mes.test", request.RequestUri.Host);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme); Assert.Equal("server-secret", request.Headers.Authorization.Parameter);
            Assert.Contains("tenantNId=tenant", request.RequestUri.Query); Assert.Contains("subjectNId=user", request.RequestUri.Query);
            return Task.FromResult(new HttpResponseMessage(Redirect ? HttpStatusCode.Redirect : HttpStatusCode.OK) { Content = new StringContent(request.RequestUri.AbsolutePath.EndsWith("metadata", StringComparison.Ordinal) ? Document.ToJsonString() : Body) });
        }
    }
}
