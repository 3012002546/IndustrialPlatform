using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SharedKernel.Topology;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;

/// <summary>
/// 种子执行端口与模型(TASK-SD-004,蓝图 §5.2/§5.3)。
/// 目标库账本由目标数据库适配器承载(不另设 ISeedLedgerStore 端口);本端口只做
/// 执行+账本读写,控制面脱敏观察由 Runner 经 IDatabaseOrchestrationStore 记录。
/// Secret 值只存内存,绝不写入控制面表/日志/事件。
/// </summary>

/// <summary>种子产物执行方式。</summary>
public enum SeedExecutorKind
{
    /// <summary>签名 SQL seed bundle,事务内执行+记账。</summary>
    SqlBundle,

    /// <summary>服务 initializer bundle,只传非敏感上下文。</summary>
    ServiceInitializer,
}

/// <summary>签名种子产物(从可信 Artifact Registry 解析;不含 Secret 值)。</summary>
public sealed record SeedArtifact(
    string ArtifactId,
    string Version,
    string Checksum,
    string? SignatureRef,
    SeedExecutorKind ExecutorKind,
    IReadOnlyList<SeedArtifactStep> Steps);

/// <summary>种子产物步骤(单条 SQL 或 initializer 指令;不含 Secret 值)。</summary>
public sealed record SeedArtifactStep(int Sequence, string StepId, string Sql, string? RollbackSql);

/// <summary>种子执行请求(仅内存传递;SecretValue 不持久化,Artifact 为已解析并校验的签名产物)。</summary>
public sealed record SeedExecutionRequest(
    SeedSet Seed,
    SeedArtifact Artifact,
    ResolvedDatabaseTarget Target,
    TargetDatabaseConnection Connection,
    string ModuleKey,
    string TenantNId,
    string EnvironmentNId,
    string? SecretValue,
    string OperationNId,
    string TraceId);

/// <summary>种子执行结果。</summary>
public sealed record SeedExecutionResult(
    bool Succeeded,
    SeedStatus Status,
    DateTimeOffset? AppliedOn,
    string? ErrorCode,
    string? ErrorSummary);

/// <summary>
/// 种子账本查询(目标库 {moduleKey}_seed_ledger)。
/// 携带已解析目标与连接(Verify 阶段经目标凭据派生),使执行器可开库读账本;
/// 连接只存内存,不进入控制面持久化。
/// </summary>
public sealed record SeedLedgerQuery(
    string TenantNId,
    string EnvironmentNId,
    string ServiceKey,
    string ModuleKey,
    string SeedKey,
    string SeedVersion,
    string OperationNId,
    string TraceId,
    ResolvedDatabaseTarget Target,
    TargetDatabaseConnection Connection);

/// <summary>种子账本条目。</summary>
public sealed record SeedLedgerEntry(
    string SeedKey,
    string SeedVersion,
    string Checksum,
    SeedScope Scope,
    SeedStatus Status,
    DateTimeOffset? AppliedOn,
    string? OperationNId);

/// <summary>种子产物解析端口。</summary>
public interface ISeedArtifactStore
{
    /// <summary>按种子产物标识解析签名产物;不存在/无效抛 <see cref="DatabaseOrchestrationRunnerException"/>(404 SD_DB_ARTIFACT_INVALID)。</summary>
    Task<SeedArtifact> ResolveAsync(string seedArtifactId, CancellationToken cancellationToken);
}

/// <summary>种子产物校验端口(allowlist + checksum + 签名引用)。</summary>
public interface ISeedArtifactVerifier
{
    /// <summary>校验种子产物真实性与完整性;false 表示拒绝执行。</summary>
    Task<bool> VerifyAsync(SeedArtifact artifact, string checksum, string? signatureRef, CancellationToken cancellationToken);
}

/// <summary>
/// 种子执行端口。实现须在目标库按 (module_key, seed_key, seed_version) 唯一幂等记账:
/// 同版本不同 checksum 拒绝(drift)、版本升级追加、失败从账本边界重试不盲重。
/// </summary>
public interface ISeedExecutor
{
    /// <summary>执行方式(SqlBundle/ServiceInitializer),供 Runner 按产物选择。</summary>
    SeedExecutorKind Kind { get; }

    /// <summary>读取目标库种子账本条目;不存在返回 <c>null</c>。</summary>
    Task<SeedLedgerEntry?> ReadLedgerAsync(SeedLedgerQuery query, CancellationToken cancellationToken);

    /// <summary>执行种子(事务内执行+记账);失败返回 <see cref="SeedExecutionResult.Succeeded"/> = false。</summary>
    Task<SeedExecutionResult> ExecuteAsync(SeedExecutionRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Secret 解析端口:按非敏感 SecretRef 解析目标环境/服务/模块的秘密值。
/// 返回值仅内存传递,绝不写入控制面表/日志/事件;解析失败返回 <c>null</c>。
/// </summary>
public interface ISeedSecretResolver
{
    /// <summary>按 SecretRef 解析秘密值;缺失/不可用返回 <c>null</c>(由调用方按 BootstrapPolicy 决策)。</summary>
    Task<string?> TryResolveAsync(
        string secretRef,
        string environmentNId,
        string serviceKey,
        string moduleKey,
        CancellationToken cancellationToken);
}
