using System.Text;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;

namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;

/// <summary>
/// 环境变量 Secret 解析器(TASK-SD-004,蓝图 §5.2/§5.3):按非敏感 SecretRef(种子键)从进程环境变量
/// 解析目标服务/模块的秘密值(如 <c>SD_SEED_SECRET_IDENTITY_ADMIN_BOOTSTRAP</c>),充当服务自有
/// Secret Provider 的本地替身。返回值仅内存传递,绝不写入控制面表/日志/事件/审计;
/// 缺失/空白返回 <c>null</c>,由调用方按 BootstrapPolicy 决策(fail-closed 默认)。
/// environmentNId/serviceKey/moduleKey 仅作作用域说明,当前命名按 SecretRef 唯一(种子键全服务唯一)。
/// </summary>
public sealed class EnvironmentSeedSecretResolver : ISeedSecretResolver
{
    /// <summary>Secret 环境变量前缀。</summary>
    private const string SecretPrefix = "SD_SEED_SECRET";

    /// <inheritdoc />
    public Task<string?> TryResolveAsync(
        string secretRef,
        string environmentNId,
        string serviceKey,
        string moduleKey,
        CancellationToken cancellationToken)
    {
        var value = Environment.GetEnvironmentVariable($"{SecretPrefix}_{SanitizeUpper(secretRef)}");
        return Task.FromResult(string.IsNullOrWhiteSpace(value) ? null : value);
    }

    private static string SanitizeUpper(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_');
        }

        return builder.ToString();
    }
}
