using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using IndustrialPlatform.Identity.Application.Bootstrap;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IndustrialPlatform.Identity.Api.Tests;

/// <summary>
/// bootstrap 端点全栈测试(§29A.5):真实 Program/控制器,替换 IBootstrapService,
/// 验证状态只含非敏感字段、readiness 形状与 SystemData 兼容、
/// 紧急恢复一次性交付与 no-store、错误信封映射。
/// </summary>
public sealed class BootstrapEndpointTests
{
    [Fact]
    public async Task Status_ReturnsNonSensitiveState()
    {
        var service = new FakeBootstrapService
        {
            Status = new BootstrapStatusResult(
                BootstrapState.Pending,
                string.Empty,
                [],
                AdminExists: false,
                MustChangePassword: false,
                CredentialDelivered: false),
        };
        using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/bootstrap/status");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = payload.RootElement.GetProperty("data");
        Assert.Equal("Pending", root.GetProperty("state").GetString());
        Assert.Equal("ADMIN", root.GetProperty("adminUserNId").GetString());
        Assert.False(root.GetProperty("adminExists").GetBoolean());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("seedVersions").ValueKind);
        // 状态端点绝不暴露 Secret
        Assert.False(root.TryGetProperty("temporaryPassword", out _));
        Assert.False(root.TryGetProperty("recoveryReference", out _));
        Assert.False(root.TryGetProperty("passwordHash", out _));
    }

    [Fact]
    public async Task Status_Ready_ExposesVersionsAndFacts()
    {
        var service = new FakeBootstrapService
        {
            Status = new BootstrapStatusResult(
                BootstrapState.Ready,
                "ID-019-03",
                [new SeedVersionStatus("identity.bootstrap-admin", "1.0.0", "Applied")],
                AdminExists: true,
                MustChangePassword: false,
                CredentialDelivered: true),
        };
        using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/bootstrap/status");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = payload.RootElement.GetProperty("data");
        Assert.Equal("Ready", root.GetProperty("state").GetString());
        Assert.Equal("ID-019-03", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("adminExists").GetBoolean());
        Assert.True(root.GetProperty("credentialDelivered").GetBoolean());
        Assert.Equal("Applied", root.GetProperty("seedVersions")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Readiness_ReturnsSystemDataCompatibleShape()
    {
        var service = new FakeBootstrapService
        {
            Readiness = new IdentityReadinessResult(
                "identity",
                "identity",
                "identity_db",
                "ID-019-03",
                BootstrapState.Ready,
                MigrationReady: true,
                RequiredSeedReady: true,
                BootstrapReady: true,
                Ready: true,
                Reason: null,
                Seeds: [new SeedVersionStatus("identity.system-catalog", "1.0.0", "Applied")]),
        };
        using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/bootstrap/readiness");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = payload.RootElement.GetProperty("data");
        Assert.Equal("identity", root.GetProperty("serviceKey").GetString());
        Assert.Equal("identity", root.GetProperty("moduleKey").GetString());
        Assert.Equal("identity_db", root.GetProperty("logicalDatabaseName").GetString());
        Assert.True(root.GetProperty("migrationReady").GetBoolean());
        Assert.True(root.GetProperty("requiredSeedReady").GetBoolean());
        Assert.True(root.GetProperty("bootstrapReady").GetBoolean());
        Assert.Equal("Ready", root.GetProperty("bootstrapStatus").GetString());
        Assert.Equal("Ready", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("ready").GetBoolean());
        Assert.Equal("Applied", root.GetProperty("seeds")[0].GetProperty("status").GetString());
        // 无连接串/路径/凭据
        Assert.False(root.TryGetProperty("connectionString", out _));
        Assert.False(root.TryGetProperty("recoveryReference", out _));
    }

    [Fact]
    public async Task Recover_ReturnsNewCredentialOnce_WithNoStore()
    {
        var service = new FakeBootstrapService
        {
            OnRecover = (_, _, _) => Task.FromResult(
                new BootstrapRecoveryResult("ADMIN", "Ab1!defghijk-2026XyZ", "rec-ref-xyz", Guid.NewGuid())),
        };
        using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/v1/bootstrap/recover",
            new StringContent(
                """{"recoveryReference":"rec-ref-xyz","approvalReference":"APPROVAL-1"}""",
                Encoding.UTF8,
                "application/json"));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        var root = payload.RootElement.GetProperty("data");
        Assert.Equal("ADMIN", root.GetProperty("userNId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("temporaryPassword").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("recoveryReference").GetString()));
    }

    [Fact]
    public async Task Recover_InvalidReference_ReturnsErrorEnvelope()
    {
        var service = new FakeBootstrapService
        {
            OnRecover = (_, _, _) => throw new BootstrapRecoveryRejectedException(),
        };
        using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/v1/bootstrap/recover",
            new StringContent(
                """{"recoveryReference":"bad","approvalReference":"APPROVAL-1"}""",
                Encoding.UTF8,
                "application/json"));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ID_BOOTSTRAP_RECOVERY_REJECTED", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Recover_PendingBootstrap_ReturnsConflictEnvelope()
    {
        var service = new FakeBootstrapService
        {
            OnRecover = (_, _, _) => throw new BootstrapPendingException(),
        };
        using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/v1/bootstrap/recover",
            new StringContent(
                """{"recoveryReference":"ref","approvalReference":"APPROVAL-1"}""",
                Encoding.UTF8,
                "application/json"));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ID_BOOTSTRAP_PENDING", payload.RootElement.GetProperty("code").GetString());
    }

    private static WebApplicationFactory<Program> CreateFactory(IBootstrapService service) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBootstrapService>();
                services.AddSingleton(service);
            }));

    private sealed class FakeBootstrapService : IBootstrapService
    {
        public BootstrapStatusResult Status { get; set; } = new(BootstrapState.Pending, string.Empty, [], false, false, false);

        public IdentityReadinessResult Readiness { get; set; } = new(
            "identity", "identity", "identity_db", string.Empty, BootstrapState.Pending, false, false, false, false, "pending", []);

        public Func<string, string, string, Task<BootstrapRecoveryResult>>? OnRecover { get; set; }

        public Task<BootstrapStatusResult> GetStatusAsync(string tenantNId, CancellationToken cancellationToken = default)
            => Task.FromResult(Status);

        public Task<IdentityReadinessResult> GetReadinessAsync(string tenantNId, CancellationToken cancellationToken = default)
            => Task.FromResult(Readiness);

        public Task<BootstrapRecoveryResult> RecoverAdminAsync(
            string tenantNId,
            string recoveryReference,
            string approvalReference,
            CancellationToken cancellationToken = default)
            => OnRecover is null
                ? throw new InvalidOperationException("测试未配置恢复行为。")
                : OnRecover(tenantNId, recoveryReference, approvalReference);
    }
}
