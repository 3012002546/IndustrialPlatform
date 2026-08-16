using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IndustrialPlatform.Identity.Api.Commands;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;

namespace IndustrialPlatform.UnifiedHost.Tests;

/// <summary>
/// UnifiedHost 集成测试:单进程组合 Identity/SystemData/ReferenceData 模块后
/// 路由可达、认证共享、模块 health/readiness 前缀命名、无重复路由/DI 冲突,
/// 以及真实 HostedService 注册下的模块迁移顺序协调(Identity → SystemData)。
/// 使用临时 SQLite;仅替换外部依赖(Redis 刷新会话存储为内存版),保留真实迁移协调路径。
/// </summary>
public sealed class UnifiedHostTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _dbPath;

    public UnifiedHostTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"industrial-platform-unifiedhost-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _dbPath = Path.Combine(_tempRoot, "unifiedhost.db");
    }

    static UnifiedHostTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // SqlSugar 连接池可能短暂占用文件句柄,忽略清理失败。
        }
    }

    /// <summary>
    /// 基础工厂:SQLite 覆盖 + 仅替换外部依赖(Redis 刷新会话存储)。
    /// 保留真实 HostedService 注册:模块迁移由 ModuleMigrationCoordinatorHostedService
    /// 在启动阶段确定顺序执行,不得通过移除托管服务掩盖迁移并发问题。
    /// </summary>
    private WebApplicationFactory<IndustrialPlatform.UnifiedHost.Program> CreateFactory()
        => new WebApplicationFactory<IndustrialPlatform.UnifiedHost.Program>()
            .WithWebHostBuilder(builder => builder
                .UseSetting("SqlSugar:ConnectionString", $"Data Source={_dbPath};Foreign Keys=True")
                .UseSetting("SqlSugar:DbType", "Sqlite")
                .ConfigureTestServices(services =>
                {
                    services.RemoveAll<IRefreshSessionStore>();
                    services.AddSingleton<IRefreshSessionStore>(new InMemoryRefreshSessionStore());
                }));

    // ---------------------------------------------------------------------------
    // 启动与路由可达
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Startup_ComposesAllModules_WithoutRouteOrDiConflicts()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal("UnifiedHost", payload.RootElement.GetProperty("service").GetString());
    }

    [Fact]
    public async Task RealHostedServices_MigrateBothModulesSequentially()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // 触发宿主启动:ModuleMigrationCoordinatorHostedService 在启动阶段同步(阻塞)完成
        // Identity → SystemData 迁移/种子,宿主启动完成后 CreateClient 才返回。
        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        // 真实 HostedService 注册:协调器是唯一迁移驱动;两套并行 SchemaMigrationBackgroundService 不得注册
        var hostedTypes = factory.Services
            .GetServices<IHostedService>()
            .Select(service => service.GetType())
            .ToList();
        Assert.Contains(hostedTypes, type => type == typeof(ModuleMigrationCoordinatorHostedService));
        Assert.DoesNotContain(hostedTypes, type => type.Name == "SchemaMigrationBackgroundService");

        // 迁移账本:Identity 迁移 + 系统目录种子已应用,SystemData 迁移已应用
        Assert.True(await TableExistsAsync("identity_schema_migrations"));
        Assert.True(await TableExistsAsync("identity_seed_ledger"));
        Assert.True(await TableExistsAsync("system_data_schema_migrations"));
        Assert.True(await CountAsync("SELECT COUNT(*) FROM identity_seed_ledger") >= 2);
        // 协调器不含 SecretBootstrap:admin 只由显式初始化创建(无 bootstrap-admin 种子账本)
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM identity_seed_ledger WHERE seed_n_id = 'identity.bootstrap-admin'"));
    }

    [Fact]
    public async Task IdentityRoutes_AreMapped()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // Identity 控制器可达:空 body 登录 → 400 校验信封
        using var loginResponse = await client.PostAsync(
            "/api/v1/auth/login",
            new StringContent("""{}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, loginResponse.StatusCode);

        // 未认证访问受保护端点 → 401 统一信封
        using var meResponse = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
    }

    [Fact]
    public async Task SystemDataRoutes_AreMapped_AndProtectedBySharedAuth()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // SystemData 控制器可达且受同一认证方案保护(未认证 401,而非 404/匿名放行)
        using var orgResponse = await client.GetAsync("/api/v1/organizations/tree");
        Assert.Equal(HttpStatusCode.Unauthorized, orgResponse.StatusCode);

        using var positionResponse = await client.GetAsync("/api/v1/positions");
        Assert.Equal(HttpStatusCode.Unauthorized, positionResponse.StatusCode);
    }

    [Fact]
    public async Task JwksEndpoint_IsServed()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/.well-known/jwks.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.True(payload.RootElement.TryGetProperty("keys", out var keys));
        Assert.True(keys.GetArrayLength() > 0);
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/definitely-not-a-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------------
    // 模块 health/readiness(前缀命名,无同名覆盖)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task LiveHealth_Returns200_WithoutDependencyChecks()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadyHealth_ListsPerModulePrefixedChecks()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // 无依赖时聚合 503,但检查清单必须包含各模块前缀命名,证明未互相覆盖
        using var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var names = payload.RootElement.GetProperty("checks")
            .EnumerateArray()
            .Select(check => check.GetProperty("name").GetString())
            .ToArray();

        Assert.Contains("identity.postgres", names);
        Assert.Contains("identity.redis", names);
        Assert.Contains("systemdata.postgres", names);
        Assert.Contains("referencedata.postgres", names);
        Assert.Contains("referencedata.rabbitmq", names);
        // 无未加前缀的同名检查(避免覆盖)
        Assert.DoesNotContain("postgres", names);
    }

    // ---------------------------------------------------------------------------
    // 认证共享:真实 HTTP 登录 → Identity 端点 200,SystemData 端点不 401
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RealLogin_ThroughUnifiedHost_ThenSystemDataUsesSameAuthentication()
    {
        using var factory = CreateFactory();
        var credentialPath = Path.Combine(_tempRoot, "bootstrap-admin.json");

        // 显式初始化:迁移 + 种子 + 内置 admin(与部署脚本同语义,SQLite 隔离)
        var exitCode = await InitializeAdminCommand.RunAsync(
            factory.Services,
            TextWriter.Null,
            credentialPath);
        Assert.Equal(0, exitCode);

        var password = JsonDocument
            .Parse(await File.ReadAllTextAsync(credentialPath))
            .RootElement.GetProperty("temporaryPassword")
            .GetString();

        using var client = factory.CreateClient();

        // 真实 HTTP 登录(Identity 模块 AuthenticationService)
        using var loginResponse = await client.PostAsync(
            "/api/v1/auth/login",
            new StringContent(
                $$"""{"loginName":"admin","password":"{{password}}"}""",
                Encoding.UTF8,
                "application/json"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        using var loginPayload = JsonDocument.Parse(await loginResponse.Content.ReadAsStreamAsync());
        Assert.True(loginPayload.RootElement.GetProperty("success").GetBoolean());

        var data = loginPayload.RootElement.GetProperty("data");
        var accessToken = data.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        // admin 权限菜单所需数据:角色与权限清单非空
        var permissionNIds = data.GetProperty("user").GetProperty("permissionNIds")
            .EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.NotEmpty(permissionNIds);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var meResponse = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        // SystemData 控制器:同一 Bearer 方案接受令牌(授权层面按权限声明裁决,不 401)
        var sdRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/organizations/tree");
        sdRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var sdResponse = await client.SendAsync(sdRequest);
        Assert.NotEqual(HttpStatusCode.Unauthorized, sdResponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, sdResponse.StatusCode);
    }

    /// <summary>内存版刷新会话存储:登录链路替代 Redis,AddAsync 成功即放行。</summary>
    private sealed class InMemoryRefreshSessionStore : IRefreshSessionStore
    {
        public Task<StoredRefreshSession?> FindByRawTokenAsync(string rawToken, CancellationToken cancellationToken)
            => Task.FromResult<StoredRefreshSession?>(null);

        public Task AddAsync(NewRefreshSession session, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<RefreshRotationStatus> RotateAsync(Guid currentSessionId, NewRefreshSession replacement, CancellationToken cancellationToken)
            => Task.FromResult(RefreshRotationStatus.Invalid);

        public Task RevokeFamilyAsync(string familyNId, string reason, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    // ---------------------------------------------------------------------------
    // SQLite 断言辅助(表名/账本为内部常量,非用户输入)
    // ---------------------------------------------------------------------------

    private async Task<bool> TableExistsAsync(string table)
    {
        using var db = CreateReadContext();
        var count = await db.SqlSugar.Ado.GetLongAsync(
            $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'");
        return count > 0;
    }

    private async Task<long> CountAsync(string sql)
    {
        using var db = CreateReadContext();
        return await db.SqlSugar.Ado.GetLongAsync(sql);
    }

    private SqlSugarDbContext CreateReadContext() =>
        new(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={_dbPath};Foreign Keys=True",
            DbType = DbType.Sqlite,
        }));
}
