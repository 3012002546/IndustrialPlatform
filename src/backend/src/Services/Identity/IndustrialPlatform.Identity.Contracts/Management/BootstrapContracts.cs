namespace IndustrialPlatform.Identity.Contracts.Management;

/// <summary>
/// bootstrap 状态响应(§29A.5 GET /api/v1/bootstrap/status)。
/// 只返回状态/版本与脱敏事实,绝不返回明文密码、引用或内部哈希。
/// State 为 BootstrapState 枚举名(Pending/Ready/RecoveryRequired)。
/// </summary>
public sealed record BootstrapStatusResponse(
    string State,
    string SchemaVersion,
    IReadOnlyList<SeedVersionDto> SeedVersions,
    string AdminUserNId,
    bool AdminExists,
    bool MustChangePassword,
    bool CredentialDelivered);

/// <summary>单个种子版本状态(非敏感)。</summary>
public sealed record SeedVersionDto(
    string SeedKey,
    string SeedVersion,
    string Status);

/// <summary>
/// 紧急恢复请求(§29A.5 POST /api/v1/bootstrap/recover)。
/// 只接受一次性恢复引用与部署审批关联,绝不接受明文密码。
/// </summary>
public sealed record BootstrapRecoverRequest(
    string? RecoveryReference,
    string? ApprovalReference);

/// <summary>
/// 紧急恢复响应:新随机临时密码只在本次响应出现一次(no-store)。
/// 不得进入通用响应日志、审计、Trace、事件或前端持久化。
/// </summary>
public sealed record BootstrapRecoverResponse(
    string UserNId,
    string TemporaryPassword,
    string RecoveryReference);

/// <summary>
/// Identity 初始化 readiness(§29A.4/§7.1.7,GET /api/v1/bootstrap/readiness)。
/// JSON 形状与 SystemData <c>ServiceInitializationReadinessV2</c> 兼容
/// (serviceKey/moduleKey/logicalDatabaseName/migrationReady/requiredSeedReady/
/// bootstrapReady/bootstrapStatus/status/reason/seeds),不含连接串、路径或凭据。
/// </summary>
public sealed record IdentityReadinessResponse(
    string ServiceKey,
    string ModuleKey,
    string LogicalDatabaseName,
    string SchemaVersion,
    string BootstrapStatus,
    string Status,
    bool MigrationReady,
    bool RequiredSeedReady,
    bool BootstrapReady,
    bool Ready,
    string? Reason,
    IReadOnlyList<SeedVersionDto> Seeds);
