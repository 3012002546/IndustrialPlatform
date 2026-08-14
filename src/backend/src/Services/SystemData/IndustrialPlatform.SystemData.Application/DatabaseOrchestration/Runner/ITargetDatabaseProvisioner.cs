using IndustrialPlatform.SystemData.Domain.Topology;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;

/// <summary>
/// 目标数据库/角色 provision 端口(05 方案 §7.1.5)。
/// 仅 provision admin 调用;幂等(数据库/角色已存在则复用);新生成凭据直接返回交由 Secret Sink 落盘。
/// </summary>
public interface ITargetDatabaseProvisioner
{
    /// <summary>确保目标数据库存在(幂等)。</summary>
    /// <param name="target">已解析物理目标。</param>
    /// <param name="admin">provision admin 连接(仅本方法内使用)。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<DatabaseProvisionOutcome> EnsureDatabaseAsync(
        ResolvedDatabaseTarget target,
        TargetDatabaseConnection admin,
        CancellationToken cancellationToken);

    /// <summary>
    /// 确保目标服务 migrator/runtime 角色与最小 grant(幂等)。
    /// 生成或复用凭据并返回;调用方负责经 <see cref="IDatabaseCredentialSink"/> 落盘。
    /// </summary>
    Task<ProvisionedRoles> EnsureRolesAsync(
        ResolvedDatabaseTarget target,
        TargetDatabaseConnection admin,
        CancellationToken cancellationToken);
}
