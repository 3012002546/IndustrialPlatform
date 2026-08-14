using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.Topology;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;

/// <summary>
/// Runner 适配器共享模型(05 方案 §7.1.5)。仅 Runner 编排核心与适配器之间传递,
/// 不进入控制面 API/日志/事件;连接串与凭据只存内存,绝不持久化到控制面表。
/// </summary>

/// <summary>目标数据库连接(单角色)。携带完整连接信息,仅限内存使用。</summary>
public sealed record TargetDatabaseConnection(
    string Provider,
    string Host,
    int Port,
    string Database,
    string? Schema,
    string? Username,
    string? Password);

/// <summary>目标数据库三类连接:provision admin / migrator / runtime。admin 仅 provision 阶段解析。</summary>
public sealed record DatabaseTargetCredentials(
    TargetDatabaseConnection? Admin,
    TargetDatabaseConnection? Migrator,
    TargetDatabaseConnection? Runtime);

/// <summary>目标数据库检查结果(inspect)。</summary>
public sealed record DatabaseTargetInspection(
    bool DatabaseExists,
    string? CurrentVersion,
    string? DatabaseIdentityFingerprint,
    IReadOnlyList<string> AppliedStepIds);

/// <summary>签名迁移产物(从可信 Artifact Registry 解析)。</summary>
public sealed record DatabaseMigrationArtifact(
    string ArtifactId,
    string Version,
    string Checksum,
    string? SignatureRef,
    bool DeclaresRecoverable,
    IReadOnlyList<DatabaseMigrationArtifactStep> Steps);

/// <summary>迁移产物步骤(单条 SQL + 可选回滚,标记破坏性)。</summary>
public sealed record DatabaseMigrationArtifactStep(
    int Sequence,
    string StepId,
    string Sql,
    string? RollbackSql,
    bool Destructive);

/// <summary>数据库 provision 结果。</summary>
public sealed record DatabaseProvisionOutcome(bool DatabaseCreated, bool DatabaseExisted);

/// <summary>已生成的目标服务角色凭据(migrator/runtime),交由 Secret Sink 落盘。</summary>
public sealed record ProvisionedRoles(TargetDatabaseConnection Migrator, TargetDatabaseConnection Runtime);

/// <summary>迁移执行结果。</summary>
public sealed record MigrationExecutionResult(int AppliedStepCount, string ResultingVersion);

/// <summary>
/// 同物理目标 advisory lock 键(EnvironmentNId + Provider + PhysicalDatabaseName)。
/// 派生 64 位键供 PostgreSQL <c>pg_advisory_lock</c> 使用;SHA-256 规范化后取前 8 字节。
/// </summary>
public sealed record DatabaseTargetLockKey(string EnvironmentNId, string Provider, string PhysicalDatabaseName)
{
    /// <summary>从已解析目标构造锁键。</summary>
    public static DatabaseTargetLockKey FromTarget(string environmentNId, ResolvedDatabaseTarget target) =>
        new(environmentNId, target.Provider.ToString(), target.PhysicalDatabaseName);

    /// <summary>规范化文本(参与锁键派生,不含 Secret)。</summary>
    public string ToCanonical() => $"{EnvironmentNId}|{Provider}|{PhysicalDatabaseName}";
}
