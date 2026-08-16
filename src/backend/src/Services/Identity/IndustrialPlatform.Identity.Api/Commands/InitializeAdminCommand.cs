using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Seeds;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.Identity.Api.Commands;

/// <summary>
/// <c>--initialize-admin</c> 显式初始化命令(§29A.4 Development 便捷入口)。
/// 只允许 Development;复用 <see cref="IdentityInitializationService.InitializeAsync"/>,
/// 不重新实现迁移/种子/密码生成。明文临时密码与一次性引用只在首次创建时交付:
/// 未提供 <c>--credential-output</c> 时沿用控制台一次性输出兼容行为;
/// 提供时写入仅当前用户可访问的 JSON 文件(stdout 只显示脱敏状态与输出路径)。
/// 命令完成后退出,不启动 Web Server。
/// </summary>
public static class InitializeAdminCommand
{
    /// <summary>命令行参数名。</summary>
    public const string ArgumentName = "--initialize-admin";

    /// <summary>一次性凭据输出文件参数名(后跟绝对路径)。</summary>
    public const string CredentialOutputArgumentName = "--credential-output";

    /// <summary>凭据 JSON 字段名(与交付契约一致)。</summary>
    internal const string JsonTenantNId = "tenantNId";
    internal const string JsonUserNId = "userNId";
    internal const string JsonLoginName = "loginName";
    internal const string JsonTemporaryPassword = "temporaryPassword";
    internal const string JsonDeliveryReference = "deliveryReference";
    internal const string JsonRecoveryReference = "recoveryReference";
    internal const string JsonDeliveryId = "deliveryId";
    internal const string JsonCreatedOnUtc = "createdOnUtc";

    /// <summary>是否请求执行初始化(参数精确匹配)。</summary>
    public static bool IsRequested(string[] args)
    {
        if (args is null || args.Length == 0)
        {
            return false;
        }

        return args.Contains(ArgumentName, StringComparer.Ordinal);
    }

    /// <summary>
    /// 解析 <c>--credential-output</c> 输出路径。未提供时返回 <c>null</c> 且无错误;
    /// 提供时必须为绝对路径且目标文件当前不存在(已存在立即拒绝),否则返回错误信息。
    /// 本方法只做参数校验,不创建文件、不访问数据库。
    /// </summary>
    public static bool TryGetCredentialOutputPath(string[] args, out string? path, out string? error)
    {
        path = null;
        error = null;

        if (args is null || args.Length == 0)
        {
            return true;
        }

        int occurrences = 0;
        string? value = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], CredentialOutputArgumentName, StringComparison.Ordinal))
            {
                continue;
            }

            occurrences++;
            if (occurrences > 1)
            {
                error = $"{CredentialOutputArgumentName} 只能指定一次。";
                return false;
            }

            if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"{CredentialOutputArgumentName} 后必须跟一个绝对路径值。";
                return false;
            }

            value = args[i + 1];
        }

        if (occurrences == 0)
        {
            return true;
        }

        if (!Path.IsPathRooted(value!))
        {
            error = $"{CredentialOutputArgumentName} 必须为绝对路径,当前值: {value}";
            return false;
        }

        if (File.Exists(value!) || Directory.Exists(value!))
        {
            error = $"{CredentialOutputArgumentName} 目标路径已存在,拒绝覆盖: {value}";
            return false;
        }

        path = value;
        return true;
    }

    /// <summary>拒绝非 Development 环境运行。</summary>
    public static void EnsureDevelopmentEnvironment(IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"{ArgumentName} 仅允许在 ASPNETCORE_ENVIRONMENT=Development 下运行,当前环境为 {environment.EnvironmentName}。");
        }
    }

    /// <summary>
    /// 执行完整初始化并输出可盘点结果。执行顺序与现有语义一致:
    /// Schema migration → SystemCatalogSeed → TenantSecuritySeed → BootstrapAdminSeed → Verify。
    /// 提供 <paramref name="credentialOutputPath"/> 时,仅当本次实际创建 admin
    /// 才写入凭据 JSON 文件;重复执行不生成、不覆盖、不重发。
    /// </summary>
    public static async Task<int> RunAsync(
        IServiceProvider services,
        TextWriter output,
        string? credentialOutputPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(output);

        var options = services.GetRequiredService<IOptions<BootstrapOptions>>().Value;
        var initialization = services.GetRequiredService<IdentityInitializationService>();

        var result = await initialization.InitializeAsync(
            new IdentitySeedContext(options.TenantNId, SystemDataOperationNId: null, TraceId: null),
            cancellationToken);

        if (credentialOutputPath is null)
        {
            // 兼容行为:控制台一次性交付。
            Print(output, result);
            return 0;
        }

        if (result.BootstrapAdmin is { } admin)
        {
            WriteCredentialFile(credentialOutputPath, admin);
            PrintWithCredentialFile(output, result, credentialOutputPath, created: true);
        }
        else
        {
            PrintWithCredentialFile(output, result, credentialOutputPath, created: false);
        }

        return 0;
    }

    /// <summary>输出可盘点账本;仅当本次实际创建 admin 时才输出一次性凭据(控制台兼容行为)。</summary>
    public static void Print(TextWriter output, IdentityInitializationResult result)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(result);

        output.WriteLine($"[{ArgumentName}] Identity 初始化完成。");
        output.WriteLine($"  TenantNId:       {result.TenantNId}");
        output.WriteLine($"  SchemaVersion:   {result.SchemaVersion}");
        output.WriteLine($"  BootstrapStatus: {result.BootstrapStatus}");
        output.WriteLine("  Seeds:");
        foreach (var seed in result.SeedVersions)
        {
            output.WriteLine($"    - {seed.SeedKey}  {seed.SeedVersion}  {seed.Status}");
        }

        if (result.BootstrapAdmin is { } admin)
        {
            output.WriteLine("  [首次初始化] 一次性凭据(仅显示一次,请立即安全保存):");
            output.WriteLine($"    LoginName:         {admin.LoginName}");
            output.WriteLine($"    TemporaryPassword: {admin.TemporaryPassword}");
            output.WriteLine($"    DeliveryReference: {admin.DeliveryReference}");
            output.WriteLine($"    RecoveryReference: {admin.RecoveryReference}");
        }
    }

    /// <summary>
    /// 使用凭据输出文件时的脱敏账本:stdout 只显示状态、种子账本、是否新建 admin 与输出路径,
    /// 绝不显示密码或一次性引用(完整引用只存在于受保护 JSON 文件中)。
    /// </summary>
    public static void PrintWithCredentialFile(TextWriter output, IdentityInitializationResult result, string credentialOutputPath, bool created)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialOutputPath);

        output.WriteLine($"[{ArgumentName}] Identity 初始化完成。");
        output.WriteLine($"  TenantNId:       {result.TenantNId}");
        output.WriteLine($"  SchemaVersion:   {result.SchemaVersion}");
        output.WriteLine($"  BootstrapStatus: {result.BootstrapStatus}");
        output.WriteLine("  Seeds:");
        foreach (var seed in result.SeedVersions)
        {
            output.WriteLine($"    - {seed.SeedKey}  {seed.SeedVersion}  {seed.Status}");
        }

        if (created)
        {
            output.WriteLine($"  [首次初始化] 已创建 admin,一次性凭据已写入(仅当前用户可读): {credentialOutputPath}");
        }
        else
        {
            output.WriteLine($"  已初始化,无新凭据。凭据输出路径(未生成文件): {credentialOutputPath}");
        }
    }

    /// <summary>
    /// 原子写入凭据 JSON:先写同目录临时文件(Windows 仅当前用户 ACL / Unix 0600),
    /// 成功后重命名;任何失败删除临时文件,不留半文件。目标文件在调用前已由
    /// <see cref="TryGetCredentialOutputPath"/> 校验不存在,此处使用 CreateNew 兜底。
    /// </summary>
    private static void WriteCredentialFile(string path, BootstrapSeedResult admin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(admin);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"凭据输出目录不存在: {directory ?? fullPath}");
        }

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = CreateSecureFileStream(tempPath))
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString(JsonTenantNId, admin.TenantNId);
                writer.WriteString(JsonUserNId, admin.UserNId);
                writer.WriteString(JsonLoginName, admin.LoginName);
                writer.WriteString(JsonTemporaryPassword, admin.TemporaryPassword);
                writer.WriteString(JsonDeliveryReference, admin.DeliveryReference);
                writer.WriteString(JsonRecoveryReference, admin.RecoveryReference);
                writer.WriteString(JsonDeliveryId, admin.DeliveryId.ToString("D", CultureInfo.InvariantCulture));
                writer.WriteString(JsonCreatedOnUtc, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            // Windows:流句柄缺少 WRITE_DAC,不能经 FileStream.SetAccessControl 设置 ACL;
            // 关闭后经 FileInfo 设置仅当前用户可访问(Unix 在创建时已 0600)。
            SecureFileForCurrentUser(tempPath);

            // 同目录原子重命名:目标不存在时 MoveFile 等价于 rename,不留半文件。
            File.Move(tempPath, fullPath);
        }
        catch
        {
            TryDeleteQuietly(tempPath);
            throw;
        }
    }

    /// <summary>创建新文件流(Windows 默认 ACL 待关闭后收紧;Unix 创建即 0600)。</summary>
    private static FileStream CreateSecureFileStream(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }

        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        });
    }

    /// <summary>Windows:将文件 ACL 收紧为仅当前用户 FullControl(无继承)。</summary>
    private static void SecureFileForCurrentUser(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser is null)
        {
            return;
        }

        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    private static void TryDeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // 清理失败不影响原始异常;半文件由脚本/调用方按退出码处理。
        }
    }
}
