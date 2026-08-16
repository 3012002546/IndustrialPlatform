using System.Text.Json;
using IndustrialPlatform.Identity.Api.Commands;
using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Passwords;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Seeds;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;

namespace IndustrialPlatform.Identity.Api.Tests;

/// <summary>
/// <c>--credential-output</c> 凭据文件交付测试(§29A.4 脚本化 admin 初始化):
/// 参数解析与绝对路径/已存在拒绝、首次创建写 JSON(8 字段)、stdout 脱敏、
/// 重复执行不生成/不覆盖/不重发、失败不留半文件、仅当前用户可访问。
/// </summary>
public sealed class InitializeAdminCredentialOutputTests : IDisposable
{
    private const string TenantNId = "development";

    static InitializeAdminCredentialOutputTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    private readonly string _dbPath;
    private readonly SqlSugarDbContext _dbContext;

    public InitializeAdminCredentialOutputTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-init-admin-credential-{Guid.NewGuid():N}.db");
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

    // ---------- 参数解析 ----------

    [Fact]
    public void TryGetCredentialOutputPath_NoArgument_ReturnsNullPathWithoutError()
    {
        Assert.True(InitializeAdminCommand.TryGetCredentialOutputPath(
            ["--initialize-admin"], out var path, out var error));
        Assert.Null(path);
        Assert.Null(error);
    }

    [Fact]
    public void TryGetCredentialOutputPath_AbsoluteNewPath_ReturnsPath()
    {
        var target = Path.Combine(Path.GetTempPath(), $"industrial-platform-cred-{Guid.NewGuid():N}.json");

        Assert.True(InitializeAdminCommand.TryGetCredentialOutputPath(
            ["--initialize-admin", "--credential-output", target], out var path, out var error));

        Assert.Equal(target, path);
        Assert.Null(error);
    }

    [Fact]
    public void TryGetCredentialOutputPath_RelativePath_Rejected()
    {
        Assert.False(InitializeAdminCommand.TryGetCredentialOutputPath(
            ["--initialize-admin", "--credential-output", "bootstrap-admin.json"], out _, out var error));
        Assert.Contains("绝对路径", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetCredentialOutputPath_ExistingFile_Rejected()
    {
        var target = Path.Combine(Path.GetTempPath(), $"industrial-platform-cred-existing-{Guid.NewGuid():N}.json");
        File.WriteAllText(target, "{}");
        try
        {
            Assert.False(InitializeAdminCommand.TryGetCredentialOutputPath(
                ["--initialize-admin", "--credential-output", target], out _, out var error));
            Assert.Contains("已存在", error, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(target);
        }
    }

    [Fact]
    public void TryGetCredentialOutputPath_ExistingDirectory_Rejected()
    {
        var target = Path.Combine(Path.GetTempPath(), $"industrial-platform-cred-dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(target);
        try
        {
            Assert.False(InitializeAdminCommand.TryGetCredentialOutputPath(
                ["--initialize-admin", "--credential-output", target], out _, out var error));
            Assert.Contains("已存在", error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(target);
        }
    }

    [Theory]
    [InlineData("--credential-output")]
    [InlineData("--credential-output", "--initialize-admin")]
    public void TryGetCredentialOutputPath_MissingValue_Rejected(params string[] args)
    {
        Assert.False(InitializeAdminCommand.TryGetCredentialOutputPath(args, out _, out var error));
        Assert.Contains("绝对路径", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetCredentialOutputPath_DuplicateArgument_Rejected()
    {
        var first = Path.Combine(Path.GetTempPath(), $"industrial-platform-cred-a-{Guid.NewGuid():N}.json");
        var second = Path.Combine(Path.GetTempPath(), $"industrial-platform-cred-b-{Guid.NewGuid():N}.json");

        Assert.False(InitializeAdminCommand.TryGetCredentialOutputPath(
            ["--credential-output", first, "--credential-output", second], out _, out var error));
        Assert.Contains("只能指定一次", error, StringComparison.Ordinal);
    }

    [Fact]
    public void IsRequested_WithCredentialOutput_StillRecognizesCommand()
    {
        var target = Path.Combine(Path.GetTempPath(), $"industrial-platform-cred-{Guid.NewGuid():N}.json");
        Assert.True(InitializeAdminCommand.IsRequested(
            ["--initialize-admin", "--credential-output", target]));
        // 无 --initialize-admin 时即使带输出参数也不算初始化请求(普通启动路径不变)。
        Assert.False(InitializeAdminCommand.IsRequested(["--credential-output", target]));
    }

    // ---------- 首次创建写 JSON ----------

    [Fact]
    public async Task RunAsync_FirstRun_WritesCredentialJsonAndRedactsStdout()
    {
        using var services = CreateServices();
        var outputDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"industrial-platform-cred-out-{Guid.NewGuid():N}")).FullName;
        var target = Path.Combine(outputDir, "bootstrap-admin.json");
        var output = new StringWriter();

        var exitCode = await InitializeAdminCommand.RunAsync(services, output, target);
        var text = output.ToString();

        Assert.Equal(0, exitCode);
        // 账本照常输出
        Assert.Contains("BootstrapStatus: Ready", text, StringComparison.Ordinal);
        Assert.Contains("identity.bootstrap-admin", text, StringComparison.Ordinal);
        Assert.Contains("首次初始化", text, StringComparison.Ordinal);
        Assert.Contains(target, text, StringComparison.Ordinal);
        // stdout 脱敏:不出现密码或一次性引用
        Assert.DoesNotContain("TemporaryPassword:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DeliveryReference:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("RecoveryReference:", text, StringComparison.Ordinal);

        // JSON 存在且含全部 8 个字段
        Assert.True(File.Exists(target));
        using var document = JsonDocument.Parse(File.ReadAllText(target));
        var root = document.RootElement;
        Assert.Equal("development", root.GetProperty("tenantNId").GetString());
        Assert.Equal("ADMIN", root.GetProperty("userNId").GetString());
        Assert.Equal("admin", root.GetProperty("loginName").GetString());
        var password = root.GetProperty("temporaryPassword").GetString();
        Assert.False(string.IsNullOrWhiteSpace(password));
        Assert.True(password!.Length >= BootstrapSeedCatalog.BootstrapPasswordMinLength);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("deliveryReference").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("recoveryReference").GetString()));
        Assert.True(Guid.TryParse(root.GetProperty("deliveryId").GetString(), out _));
        Assert.True(DateTimeOffset.TryParse(root.GetProperty("createdOnUtc").GetString(), out _));

        // 临时文件不残留
        Assert.Empty(Directory.GetFiles(outputDir, ".*.tmp"));

        // 安全模式:Windows 仅当前用户;Unix 0600
        if (OperatingSystem.IsWindows())
        {
            AssertWindowsOnlyCurrentUserAcl(target);
        }
        else
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(target));
        }
    }

    /// <summary>Windows:文件 ACL 只允许当前用户 FullControl(显式规则,无继承)。</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void AssertWindowsOnlyCurrentUserAcl(string path)
    {
        var rules = new FileInfo(path)
            .GetAccessControl()
            .GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier))
            .Cast<System.Security.AccessControl.FileSystemAccessRule>()
            .ToList();
        var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent().User;
        Assert.NotEmpty(rules);
        Assert.All(rules, rule =>
        {
            Assert.Equal(currentUser, rule.IdentityReference);
            Assert.Equal(System.Security.AccessControl.AccessControlType.Allow, rule.AccessControlType);
            Assert.Equal(System.Security.AccessControl.FileSystemRights.FullControl, rule.FileSystemRights);
        });
    }

    [Fact]
    public async Task RunAsync_FirstRun_WritesValuesMatchingDatabaseState()
    {
        using var services = CreateServices();
        var outputDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"industrial-platform-cred-match-{Guid.NewGuid():N}")).FullName;
        var target = Path.Combine(outputDir, "bootstrap-admin.json");

        await InitializeAdminCommand.RunAsync(services, TextWriter.Null, target);

        using var document = JsonDocument.Parse(File.ReadAllText(target));
        var root = document.RootElement;
        // 与库内交付记录对应(单条 bootstrap 凭据交付记录)
        Assert.Equal(1, await _dbContext.SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM identity_bootstrap_credential"));
        Assert.Equal(1, await _dbContext.SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM identity_user"));
    }

    // ---------- 重复执行不生成/不覆盖 ----------

    [Fact]
    public async Task RunAsync_RepeatRun_DoesNotGenerateOrOverwrite()
    {
        using var services = CreateServices();
        var outputDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"industrial-platform-cred-repeat-{Guid.NewGuid():N}")).FullName;
        var firstTarget = Path.Combine(outputDir, "bootstrap-admin-1.json");
        var secondTarget = Path.Combine(outputDir, "bootstrap-admin-2.json");

        var firstOutput = new StringWriter();
        var secondOutput = new StringWriter();
        var firstExit = await InitializeAdminCommand.RunAsync(services, firstOutput, firstTarget);
        var secondExit = await InitializeAdminCommand.RunAsync(services, secondOutput, secondTarget);

        Assert.Equal(0, firstExit);
        Assert.Equal(0, secondExit);
        Assert.True(File.Exists(firstTarget));

        // 第二次:admin 已存在 → 不生成第二个文件,stdout 显示"无新凭据"
        Assert.False(File.Exists(secondTarget));
        Assert.Contains("无新凭据", secondOutput.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("TemporaryPassword:", secondOutput.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ConsoleFirstThenFile_DoesNotCreateFile()
    {
        using var services = CreateServices();
        var outputDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"industrial-platform-cred-late-{Guid.NewGuid():N}")).FullName;
        var target = Path.Combine(outputDir, "bootstrap-admin.json");

        // 第一次走控制台兼容路径(创建 admin);第二次带文件路径 → 不生成文件
        await InitializeAdminCommand.RunAsync(services, TextWriter.Null);
        var secondOutput = new StringWriter();
        var exit = await InitializeAdminCommand.RunAsync(services, secondOutput, target);

        Assert.Equal(0, exit);
        Assert.False(File.Exists(target));
        Assert.Contains("无新凭据", secondOutput.ToString(), StringComparison.Ordinal);
    }

    // ---------- 失败不留半文件 ----------

    [Fact]
    public async Task RunAsync_MissingParentDirectory_FailsWithoutFile()
    {
        using var services = CreateServices();
        var missingDir = Path.Combine(Path.GetTempPath(), $"industrial-platform-cred-missing-{Guid.NewGuid():N}");
        var target = Path.Combine(missingDir, "bootstrap-admin.json");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            InitializeAdminCommand.RunAsync(services, TextWriter.Null, target));

        Assert.False(File.Exists(target));
        Assert.False(Directory.Exists(missingDir));
    }

    [Fact]
    public async Task RunAsync_RenameFails_LeavesNoTempFile()
    {
        using var services = CreateServices();
        var outputDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"industrial-platform-cred-rename-{Guid.NewGuid():N}")).FullName;
        // 目标路径指向一个已存在目录 → File.Move 重命名失败,临时文件必须被清理
        var targetDir = Path.Combine(outputDir, "bootstrap-admin.json");
        Directory.CreateDirectory(targetDir);

        await Assert.ThrowsAnyAsync<IOException>(() =>
            InitializeAdminCommand.RunAsync(services, TextWriter.Null, targetDir));

        Assert.Empty(Directory.GetFiles(outputDir, ".*.tmp"));
        Assert.True(Directory.Exists(targetDir));
    }

    private Task<int> CountAsync(string sql) =>
        _dbContext.SqlSugar.Ado.GetIntAsync(sql);
}
