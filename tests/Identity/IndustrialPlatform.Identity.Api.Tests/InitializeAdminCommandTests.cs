using System.Text;
using IndustrialPlatform.Identity.Api.Commands;
using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Passwords;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Seeds;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;

namespace IndustrialPlatform.Identity.Api.Tests;

/// <summary>
/// <c>--initialize-admin</c> 命令测试(§29A.4 Development 便捷入口):
/// 参数识别、非 Development 拒绝、首次创建输出一次性凭据、
/// 重复执行不重发凭据、普通启动路径不创建 admin。
/// (PostgreSQL 真实验证标记「待验收」,与既有种子测试一致。)
/// </summary>
public sealed class InitializeAdminCommandTests : IDisposable
{
    private const string TenantNId = "development";

    static InitializeAdminCommandTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    private readonly string _dbPath;
    private readonly SqlSugarDbContext _dbContext;

    public InitializeAdminCommandTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-init-admin-{Guid.NewGuid():N}.db");
        _dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={_dbPath};Foreign Keys=True",
            DbType = DbType.Sqlite,
        }));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
            // SqlSugarScope 连接池可能短暂占用文件句柄,忽略清理失败。
        }
    }

    private ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddOptions<BootstrapOptions>().Configure(options => options.TenantNId = TenantNId);
        services.AddSingleton(new IdentityInitializationService(
            new SchemaMigrationRunner(_dbContext, IdentitySchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance),
            new IdentitySeedRunner(_dbContext, new BcryptPasswordHasher(), new BootstrapCredentialStore(_dbContext)),
            new BootstrapStore(_dbContext, new UserRepository(_dbContext))));
        return services.BuildServiceProvider();
    }

    private static Task<int> RunOnceAsync(ServiceProvider services, TextWriter output) =>
        InitializeAdminCommand.RunAsync(services, output);

    [Theory]
    [InlineData("--initialize-admin")]
    [InlineData("--initialize-admin", "extra")]
    [InlineData("run", "--initialize-admin")]
    public void IsRequested_RecognizesExactArgument(params string[] args)
    {
        Assert.True(InitializeAdminCommand.IsRequested(args));
    }

    [Theory]
    [InlineData("")]
    [InlineData("--other")]
    [InlineData("--initialize")]
    [InlineData("--initialize-admin-extra")]
    public void IsRequested_DoesNotMatchOtherArguments(params string[] args)
    {
        Assert.False(InitializeAdminCommand.IsRequested(args));
    }

    [Fact]
    public void IsRequested_NullArgs_ReturnsFalse()
    {
        Assert.False(InitializeAdminCommand.IsRequested(null!));
    }

    [Fact]
    public void EnsureDevelopmentEnvironment_AcceptsDevelopment()
    {
        var environment = new FakeWebHostEnvironment { EnvironmentName = "Development" };

        // 到达此处即证明未抛出异常
        InitializeAdminCommand.EnsureDevelopmentEnvironment(environment);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    public void EnsureDevelopmentEnvironment_RejectsNonDevelopment(string environmentName)
    {
        var environment = new FakeWebHostEnvironment { EnvironmentName = environmentName };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            InitializeAdminCommand.EnsureDevelopmentEnvironment(environment));
        Assert.Contains("Development", exception.Message, StringComparison.Ordinal);
        Assert.Contains(environmentName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_FirstRun_PrintsLedgerAndOneTimeCredentials()
    {
        using var services = CreateServices();
        var output = new StringWriter();

        var exitCode = await RunOnceAsync(services, output);
        var text = output.ToString();

        Assert.Equal(0, exitCode);
        // 可盘点账本
        Assert.Contains("TenantNId:       development", text, StringComparison.Ordinal);
        Assert.Contains("BootstrapStatus: Ready", text, StringComparison.Ordinal);
        Assert.Contains("identity.system-catalog", text, StringComparison.Ordinal);
        Assert.Contains("identity.tenant-security", text, StringComparison.Ordinal);
        Assert.Contains("identity.bootstrap-admin", text, StringComparison.Ordinal);
        Assert.Contains("Applied", text, StringComparison.Ordinal);
        // 首次创建的一次性凭据
        Assert.Contains("LoginName:", text, StringComparison.Ordinal);
        Assert.Contains("TemporaryPassword:", text, StringComparison.Ordinal);
        Assert.Contains("DeliveryReference:", text, StringComparison.Ordinal);
        Assert.Contains("RecoveryReference:", text, StringComparison.Ordinal);

        // 库中只有单 admin;明文密码不落库
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM identity_user"));
        var storedHash = await _dbContext.SqlSugar.Ado.GetStringAsync("SELECT password_hash FROM identity_user WHERE n_id = 'ADMIN'");
        Assert.StartsWith("$2", storedHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_RepeatRun_DoesNotReissueCredentials()
    {
        using var services = CreateServices();
        var firstOutput = new StringWriter();
        var secondOutput = new StringWriter();

        var firstExitCode = await RunOnceAsync(services, firstOutput);
        var secondExitCode = await RunOnceAsync(services, secondOutput);
        var firstText = firstOutput.ToString();
        var secondText = secondOutput.ToString();

        Assert.Equal(0, firstExitCode);
        Assert.Equal(0, secondExitCode);

        // 第二次仍输出可盘点账本与 Ready
        Assert.Contains("BootstrapStatus: Ready", secondText, StringComparison.Ordinal);
        Assert.Contains("identity.bootstrap-admin", secondText, StringComparison.Ordinal);
        Assert.Contains("Applied", secondText, StringComparison.Ordinal);
        // 但绝不再输出密码 / 一次性引用
        Assert.DoesNotContain("TemporaryPassword:", secondText, StringComparison.Ordinal);
        Assert.DoesNotContain("DeliveryReference:", secondText, StringComparison.Ordinal);
        Assert.DoesNotContain("RecoveryReference:", secondText, StringComparison.Ordinal);
        Assert.DoesNotContain("LoginName:", secondText, StringComparison.Ordinal);

        // 数据幂等:单 admin、单交付记录,密码未被覆盖
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM identity_user"));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM identity_bootstrap_credential"));
        Assert.Equal(3, await CountAsync("SELECT COUNT(*) FROM identity_seed_ledger"));
    }

    [Fact]
    public async Task NormalStartupCatalogPath_DoesNotCreateAdmin()
    {
        // 镜像 SchemaMigrationBackgroundService:迁移 + SystemCatalog/TenantSecurity,
        // 不含 SecretBootstrap(includeBootstrapAdmin: false)。
        await new SchemaMigrationRunner(_dbContext, IdentitySchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance)
            .ApplyPendingAsync();
        await new IdentitySeedRunner(_dbContext, new BcryptPasswordHasher(), new BootstrapCredentialStore(_dbContext))
            .RunAsync(new IdentitySeedContext(TenantNId, SystemDataOperationNId: null, TraceId: null), includeBootstrapAdmin: false);

        // 普通启动不创建 admin,也无 bootstrap-admin 种子账本
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM identity_user"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM identity_seed_ledger WHERE seed_n_id = 'identity.bootstrap-admin'"));
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM identity_seed_ledger"));
    }

    [Fact]
    public async Task Print_OnlyIncludesCredentialsWhenAdminWasActuallyCreated()
    {
        var first = await new IdentityInitializationService(
            new SchemaMigrationRunner(_dbContext, IdentitySchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance),
            new IdentitySeedRunner(_dbContext, new BcryptPasswordHasher(), new BootstrapCredentialStore(_dbContext)),
            new BootstrapStore(_dbContext, new UserRepository(_dbContext)))
            .InitializeAsync(new IdentitySeedContext(TenantNId, SystemDataOperationNId: null, TraceId: null));

        var withCredential = new StringWriter();
        InitializeAdminCommand.Print(withCredential, first);
        var withCredentialText = withCredential.ToString();

        Assert.Contains(first.BootstrapAdmin!.LoginName, withCredentialText, StringComparison.Ordinal);
        Assert.Contains(first.BootstrapAdmin.TemporaryPassword, withCredentialText, StringComparison.Ordinal);
        Assert.Contains(first.BootstrapAdmin.DeliveryReference, withCredentialText, StringComparison.Ordinal);
        Assert.Contains(first.BootstrapAdmin.RecoveryReference, withCredentialText, StringComparison.Ordinal);

        // 幂等结果(BootstrapAdmin 为 null)只输出账本,不含任何一次性值
        var withoutCredential = new StringWriter();
        InitializeAdminCommand.Print(withoutCredential, first with { BootstrapAdmin = null });
        var withoutCredentialText = withoutCredential.ToString();

        Assert.DoesNotContain("TemporaryPassword:", withoutCredentialText, StringComparison.Ordinal);
        Assert.DoesNotContain("DeliveryReference:", withoutCredentialText, StringComparison.Ordinal);
        Assert.DoesNotContain("RecoveryReference:", withoutCredentialText, StringComparison.Ordinal);
        Assert.DoesNotContain(first.BootstrapAdmin.TemporaryPassword, withoutCredentialText, StringComparison.Ordinal);
        Assert.DoesNotContain(first.BootstrapAdmin.RecoveryReference, withoutCredentialText, StringComparison.Ordinal);
        Assert.Contains("BootstrapStatus: Ready", withoutCredentialText, StringComparison.Ordinal);
    }

    private Task<int> CountAsync(string sql) =>
        _dbContext.SqlSugar.Ado.GetIntAsync(sql);

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "IndustrialPlatform.Identity.Api.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public string EnvironmentName { get; set; } = "Development";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
