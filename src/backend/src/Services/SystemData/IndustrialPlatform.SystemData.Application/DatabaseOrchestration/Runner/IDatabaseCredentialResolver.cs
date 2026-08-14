using IndustrialPlatform.SystemData.Domain.Topology;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;

/// <summary>
/// 目标凭据解析端口(05 方案 §7.1.5)。provision admin 只从环境变量/容器/Kubernetes Secret 或既有
/// Secret Provider 临时解析;migrator/runtime 从 Secret Sink 或可信源解析。控制面绝不保存凭据值。
/// 解析失败抛 <see cref="DatabaseOrchestrationException"/>(SD_DB_SECRET_UNAVAILABLE)。
/// </summary>
public interface IDatabaseCredentialResolver
{
    /// <summary>解析目标三类连接。</summary>
    /// <param name="target">已解析物理目标。</param>
    /// <param name="provisionRequired">是否需 provision admin(Provision 阶段才解析,避免不必要暴露)。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<DatabaseTargetCredentials> ResolveAsync(
        ResolvedDatabaseTarget target,
        bool provisionRequired,
        CancellationToken cancellationToken);
}
