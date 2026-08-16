using IndustrialPlatform.SharedKernel.Topology;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;

/// <summary>
/// 目标 Secret Sink 端口(05 方案 §7.1.5)。新生成的 migrator/runtime 凭据直接写入配置的
/// Secret Sink(文件/云 Secret);控制面 API 只返回 credential version/fingerprint,绝不回传值。
/// </summary>
public interface IDatabaseCredentialSink
{
    /// <summary>将新生成的目标角色凭据写入 Secret Sink(幂等覆盖)。</summary>
    Task WriteAsync(
        ResolvedDatabaseTarget target,
        ProvisionedRoles roles,
        CancellationToken cancellationToken);
}
