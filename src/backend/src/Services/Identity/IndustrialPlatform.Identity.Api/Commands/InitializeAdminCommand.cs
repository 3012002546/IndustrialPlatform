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
/// 不重新实现迁移/种子/密码生成。明文临时密码与一次性引用只在首次创建时输出到命令控制台,
/// 绝不进入日志、数据库、Trace、审计或异常信息。命令完成后退出,不启动 Web Server。
/// </summary>
public static class InitializeAdminCommand
{
    /// <summary>命令行参数名。</summary>
    public const string ArgumentName = "--initialize-admin";

    /// <summary>是否请求执行初始化(参数精确匹配)。</summary>
    public static bool IsRequested(string[] args)
    {
        if (args is null || args.Length == 0)
        {
            return false;
        }

        return args.Contains(ArgumentName, StringComparer.Ordinal);
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
    /// </summary>
    public static async Task<int> RunAsync(
        IServiceProvider services,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(output);

        var options = services.GetRequiredService<IOptions<BootstrapOptions>>().Value;
        var initialization = services.GetRequiredService<IdentityInitializationService>();

        var result = await initialization.InitializeAsync(
            new IdentitySeedContext(options.TenantNId, SystemDataOperationNId: null, TraceId: null),
            cancellationToken);

        Print(output, result);
        return 0;
    }

    /// <summary>输出可盘点账本;仅当本次实际创建 admin 时才输出一次性凭据。</summary>
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
}
