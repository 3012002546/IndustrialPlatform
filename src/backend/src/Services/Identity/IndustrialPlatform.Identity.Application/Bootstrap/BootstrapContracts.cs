namespace IndustrialPlatform.Identity.Application.Bootstrap;

/// <summary>
/// bootstrap 整体状态(§29A.4)。<see cref="Ready"/> 表示 admin 已创建且健康,
/// readiness 不以 admin 是否改密为条件。
/// </summary>
public enum BootstrapState
{
    /// <summary>初始化未完成:Schema/Seed 未就绪或 admin 尚未创建。</summary>
    Pending = 0,

    /// <summary>admin 已创建且健康(活动、拥有 SYSTEM_ADMIN 角色)。</summary>
    Ready = 1,

    /// <summary>admin 存在但异常(禁用/删除/失去系统角色/凭据遗失),必须走审计化紧急恢复。</summary>
    RecoveryRequired = 2,
}

/// <summary>凭据交付状态机(§29A.4):Pending → Delivered(一次性领取)或 Recovered(紧急恢复)。</summary>
public enum BootstrapDeliveryState
{
    /// <summary>已生成,尚未交付。</summary>
    Pending = 0,

    /// <summary>已在受保护响应中交付一次。</summary>
    Delivered = 1,

    /// <summary>已通过紧急恢复生成新凭据,本记录作废。</summary>
    Recovered = 2,

    /// <summary>已吊销。</summary>
    Revoked = 3,
}

/// <summary>单个种子的账本状态(§29A.4)。</summary>
public sealed record SeedVersionStatus(string SeedKey, string SeedVersion, string Status);

/// <summary>
/// bootstrap 凭据交付记录(应用层视图)。只含引用哈希与状态,绝不含明文密码。
/// </summary>
public sealed record BootstrapCredentialDelivery(
    Guid DeliveryId,
    string TenantNId,
    string UserNId,
    BootstrapDeliveryState State,
    string? DeliveryReferenceHash,
    string RecoveryReferenceHash,
    DateTimeOffset? DeliveredOn,
    DateTimeOffset? RecoveredOn);

/// <summary>
/// bootstrap admin 聚合快照(仅状态,无敏感字段)。
/// </summary>
public sealed record BootstrapAdminSnapshot(
    string UserNId,
    bool Exists,
    bool IsDeleted,
    bool IsActive,
    bool MustChangePassword,
    bool HasSystemAdminRole,
    int AuthVersion);

/// <summary>
/// bootstrap 状态查询结果(§29A.5 GET /bootstrap/status):只含状态/版本,不含 Secret。
/// </summary>
public sealed record BootstrapStatusResult(
    BootstrapState State,
    string SchemaVersion,
    IReadOnlyList<SeedVersionStatus> SeedVersions,
    bool AdminExists,
    bool MustChangePassword,
    bool CredentialDelivered);

/// <summary>
/// Identity 初始化 readiness(与 SystemData <c>ServiceInitializationReadinessV2</c> JSON 形状兼容;
/// Identity 侧自持等价形状,不引用 SystemData 程序集)。
/// </summary>
public sealed record IdentityReadinessResult(
    string ServiceKey,
    string ModuleKey,
    string LogicalDatabaseName,
    string SchemaVersion,
    BootstrapState BootstrapStatus,
    bool MigrationReady,
    bool RequiredSeedReady,
    bool BootstrapReady,
    bool Ready,
    string? Reason,
    IReadOnlyList<SeedVersionStatus> Seeds);

/// <summary>
/// 紧急恢复结果(§29A.5 POST /bootstrap/recover):新临时密码只在本次受保护响应出现一次。
/// </summary>
public sealed record BootstrapRecoveryResult(
    string UserNId,
    string TemporaryPassword,
    string RecoveryReference,
    Guid DeliveryId);
