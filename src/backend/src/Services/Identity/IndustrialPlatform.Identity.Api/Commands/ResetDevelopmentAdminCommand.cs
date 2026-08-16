using IndustrialPlatform.Identity.Infrastructure.Bootstrap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using IndustrialPlatform.Identity.Application.Bootstrap;

namespace IndustrialPlatform.Identity.Api.Commands;

/// <summary>仅 Development 可用的显式 admin 密码重置命令。</summary>
public static class ResetDevelopmentAdminCommand
{
    public const string ArgumentName = "--reset-development-admin";

    public static bool IsRequested(string[] args) =>
        args?.Contains(ArgumentName, StringComparer.Ordinal) == true;

    public static async Task<int> RunAsync(
        IServiceProvider services,
        TextWriter output,
        string credentialOutputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialOutputPath);

        try
        {
            var tenantNId = services.GetRequiredService<IOptions<BootstrapOptions>>().Value.TenantNId;
            var reset = services.GetRequiredService<DevelopmentAdminResetService>();
            var result = await reset.ResetAsync(tenantNId, cancellationToken);
            InitializeAdminCommand.WriteCredentialFile(credentialOutputPath, result);

            output.WriteLine($"[{ArgumentName}] admin 临时密码已重置。");
            output.WriteLine($"  TenantNId: {result.TenantNId}");
            output.WriteLine($"  UserNId:   {result.UserNId}");
            output.WriteLine($"  LoginName: {result.LoginName}");
            output.WriteLine($"  一次性凭据已写入(仅当前用户可读): {credentialOutputPath}");
            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"[{ArgumentName}] 重置失败: {ex.Message}");
            return 1;
        }
    }
}
