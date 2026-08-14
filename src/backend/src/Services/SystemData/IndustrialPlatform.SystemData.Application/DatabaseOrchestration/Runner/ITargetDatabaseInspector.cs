using IndustrialPlatform.SystemData.Domain.Topology;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;

/// <summary>
/// 目标数据库检查端口(05 方案 §7.1.4 Inspect/Verify 阶段)。
/// 返回存在性、当前迁移版本、身份指纹与已应用步骤标识;不返回任何 Secret/连接细节。
/// </summary>
public interface ITargetDatabaseInspector
{
    /// <summary>检查目标数据库(只读,不修改目标)。</summary>
    /// <param name="target">已解析物理目标。</param>
    /// <param name="credentials">目标凭据(migrator/runtime,取决于角色)。</param>
    /// <param name="moduleKey">模块标识(账本表名前缀 {moduleKey}_schema_migrations 的派生依据)。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<DatabaseTargetInspection> InspectAsync(
        ResolvedDatabaseTarget target,
        DatabaseTargetCredentials credentials,
        string moduleKey,
        CancellationToken cancellationToken);
}
