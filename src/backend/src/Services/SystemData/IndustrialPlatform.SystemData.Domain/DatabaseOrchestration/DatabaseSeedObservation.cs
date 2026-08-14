using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

/// <summary>
/// 种子观察记录聚合根(TASK-SD-004,蓝图 §5.3,控制面 <c>system_data_seed_observation</c>)。
/// 记录对某 (ServiceKey, ModuleKey, SeedKey) 的最近一次观察(版本/校验和/状态)。
/// 只追加、创建即固化(<see cref="Entity.IsFrozen"/> = true);本地账本是权威,
/// SystemData 只保存脱敏观察。不含种子内容、Secret、SQL 或任何凭据。
/// </summary>
public sealed class DatabaseSeedObservation : AggregateRoot
{
    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; private set; }

    /// <summary>环境业务标识。</summary>
    public string EnvironmentNId { get; private set; }

    /// <summary>服务稳定键。</summary>
    public string ServiceKey { get; private set; }

    /// <summary>模块标识。</summary>
    public string ModuleKey { get; private set; }

    /// <summary>稳定种子键。</summary>
    public string SeedKey { get; private set; }

    /// <summary>种子版本。</summary>
    public string SeedVersion { get; private set; }

    /// <summary>种子产物校验和(SHA-256 十六进制)。</summary>
    public string Checksum { get; private set; }

    /// <summary>种子作用域。</summary>
    public SeedScope Scope { get; private set; }

    /// <summary>种子状态。</summary>
    public SeedStatus Status { get; private set; }

    /// <summary>应用时间(仅 Applied)。</summary>
    public DateTimeOffset? AppliedOn { get; private set; }

    /// <summary>产生该观察的操作业务标识(可为空)。</summary>
    public string? OperationNId { get; private set; }

    /// <summary>验证状态。</summary>
    public VerificationStatus VerificationStatus { get; private set; }

    private DatabaseSeedObservation()
    {
        TenantNId = string.Empty;
        EnvironmentNId = string.Empty;
        ServiceKey = string.Empty;
        ModuleKey = string.Empty;
        SeedKey = string.Empty;
        SeedVersion = string.Empty;
        Checksum = string.Empty;
    }

    private DatabaseSeedObservation(
        string tenantNId,
        string environmentNId,
        string serviceKey,
        string moduleKey,
        string seedKey,
        string seedVersion,
        string checksum,
        SeedScope scope,
        SeedStatus status,
        DateTimeOffset? appliedOn,
        string? operationNId,
        VerificationStatus verificationStatus)
    {
        TenantNId = DatabaseOrchestrationGuard.RequireNId(tenantNId, "种子观察的租户标识不能为空。");
        EnvironmentNId = DatabaseOrchestrationGuard.RequireNId(environmentNId, "种子观察的环境标识不能为空。");
        ServiceKey = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            serviceKey, "服务键不能为空。", DatabaseRegistration.ServiceKeyMaxLength, $"服务键长度不能超过 {DatabaseRegistration.ServiceKeyMaxLength} 个字符。");
        ModuleKey = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            moduleKey, "模块标识不能为空。", DatabaseRegistration.ModuleKeyMaxLength, $"模块标识长度不能超过 {DatabaseRegistration.ModuleKeyMaxLength} 个字符。");
        SeedKey = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            seedKey, "种子键不能为空。", SeedSet.SeedKeyMaxLength, $"种子键长度不能超过 {SeedSet.SeedKeyMaxLength} 个字符。");
        SeedVersion = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            seedVersion, "种子版本不能为空。", SeedSet.SeedVersionMaxLength, $"种子版本长度不能超过 {SeedSet.SeedVersionMaxLength} 个字符。");
        Checksum = DatabaseOrchestrationGuard.RequireSha256Hex(checksum, "种子校验和不能为空。");
        Scope = scope;
        Status = status;
        AppliedOn = appliedOn;
        OperationNId = operationNId is null
            ? null
            : DatabaseOrchestrationGuard.RequireNId(operationNId, "操作标识不能为空。");
        VerificationStatus = verificationStatus;
        IsFrozen = true;
    }

    /// <summary>持久化层重建专用构造,不重新校验。</summary>
    internal DatabaseSeedObservation(
        Guid id,
        string tenantNId,
        string environmentNId,
        string serviceKey,
        string moduleKey,
        string seedKey,
        string seedVersion,
        string checksum,
        SeedScope scope,
        SeedStatus status,
        DateTimeOffset? appliedOn,
        string? operationNId,
        VerificationStatus verificationStatus,
        bool isFrozen,
        bool isLocked,
        bool isDeleted,
        string entityType,
        DateTimeOffset createdOn,
        DateTimeOffset lastUpdatedOn,
        long optimisticVersion,
        Guid concurrencyVersion)
        : base()
    {
        Id = id;
        TenantNId = tenantNId;
        EnvironmentNId = environmentNId;
        ServiceKey = serviceKey;
        ModuleKey = moduleKey;
        SeedKey = seedKey;
        SeedVersion = seedVersion;
        Checksum = checksum;
        Scope = scope;
        Status = status;
        AppliedOn = appliedOn;
        OperationNId = operationNId;
        VerificationStatus = verificationStatus;
        IsFrozen = isFrozen;
        IsLocked = isLocked;
        IsDeleted = isDeleted;
        EntityType = entityType;
        CreatedOn = createdOn;
        LastUpdatedOn = lastUpdatedOn;
        OptimisticVersion = optimisticVersion;
        ConcurrencyVersion = concurrencyVersion;
    }

    /// <summary>记录一次种子观察(不可变,创建即固化)。</summary>
    public static DatabaseSeedObservation Record(
        string tenantNId,
        string environmentNId,
        string serviceKey,
        string moduleKey,
        string seedKey,
        string seedVersion,
        string checksum,
        SeedScope scope,
        SeedStatus status,
        DateTimeOffset? appliedOn,
        string? operationNId,
        VerificationStatus verificationStatus)
        => new(
            tenantNId,
            environmentNId,
            serviceKey,
            moduleKey,
            seedKey,
            seedVersion,
            checksum,
            scope,
            status,
            appliedOn,
            operationNId,
            verificationStatus);
}
