using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

/// <summary>
/// 迁移观察记录聚合根(05 方案 §8.1 <c>system_data_database_migration_observation</c>)。
/// 记录对某数据库身份的最近一次观察(版本/产物校验和),只追加、创建即固化;
/// 用于 readiness 最近观察版本与 SD-003 verify 后回写。不含任何 Secret。
/// </summary>
public sealed class DatabaseMigrationObservation : AggregateRoot
{
    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; private set; }

    /// <summary>环境业务标识。</summary>
    public string EnvironmentNId { get; private set; }

    /// <summary>服务稳定键。</summary>
    public string ServiceKey { get; private set; }

    /// <summary>数据库身份指纹(由 <see cref="Topology.DatabaseTopologyFingerprint.ComputeDatabaseIdentityFingerprint"/> 计算)。</summary>
    public string DatabaseIdentityFingerprint { get; private set; }

    /// <summary>观察到的迁移版本。</summary>
    public string ObservedVersion { get; private set; }

    /// <summary>观察到的产物校验和。</summary>
    public string ArtifactChecksum { get; private set; }

    /// <summary>观察时间。</summary>
    public DateTimeOffset ObservedOn { get; private set; }

    /// <summary>产生该观察的操作业务标识(可为空)。</summary>
    public string? OperationNId { get; private set; }

    /// <summary>验证状态。</summary>
    public VerificationStatus VerificationStatus { get; private set; }

    private DatabaseMigrationObservation()
    {
        TenantNId = string.Empty;
        EnvironmentNId = string.Empty;
        ServiceKey = string.Empty;
        DatabaseIdentityFingerprint = string.Empty;
        ObservedVersion = string.Empty;
        ArtifactChecksum = string.Empty;
    }

    private DatabaseMigrationObservation(
        string tenantNId,
        string environmentNId,
        string serviceKey,
        string databaseIdentityFingerprint,
        string observedVersion,
        string artifactChecksum,
        DateTimeOffset observedOn,
        string? operationNId,
        VerificationStatus verificationStatus)
    {
        TenantNId = DatabaseOrchestrationGuard.RequireNId(tenantNId, "迁移观察的租户标识不能为空。");
        EnvironmentNId = DatabaseOrchestrationGuard.RequireNId(environmentNId, "迁移观察的环境标识不能为空。");
        ServiceKey = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            serviceKey, "服务键不能为空。", DatabaseRegistration.ServiceKeyMaxLength, $"服务键长度不能超过 {DatabaseRegistration.ServiceKeyMaxLength} 个字符。");
        DatabaseIdentityFingerprint = DatabaseOrchestrationGuard.RequireSha256Hex(databaseIdentityFingerprint, "数据库身份指纹不能为空。");
        ObservedVersion = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            observedVersion, "观察版本不能为空。", DatabaseProvisionPlan.VersionMaxLength, $"观察版本长度不能超过 {DatabaseProvisionPlan.VersionMaxLength} 个字符。");
        ArtifactChecksum = DatabaseOrchestrationGuard.RequireSha256Hex(artifactChecksum, "产物校验和不能为空。");
        ObservedOn = observedOn;
        OperationNId = operationNId is null
            ? null
            : DatabaseOrchestrationGuard.RequireNId(operationNId, "操作标识不能为空。");
        VerificationStatus = verificationStatus;
    }

    /// <summary>持久化层重建专用构造,不重新校验。</summary>
    internal DatabaseMigrationObservation(
        Guid id,
        string tenantNId,
        string environmentNId,
        string serviceKey,
        string databaseIdentityFingerprint,
        string observedVersion,
        string artifactChecksum,
        DateTimeOffset observedOn,
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
        DatabaseIdentityFingerprint = databaseIdentityFingerprint;
        ObservedVersion = observedVersion;
        ArtifactChecksum = artifactChecksum;
        ObservedOn = observedOn;
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

    /// <summary>记录一次迁移观察(不可变)。</summary>
    public static DatabaseMigrationObservation Record(
        string tenantNId,
        string environmentNId,
        string serviceKey,
        string databaseIdentityFingerprint,
        string observedVersion,
        string artifactChecksum,
        DateTimeOffset observedOn,
        string? operationNId,
        VerificationStatus verificationStatus)
        => new(
            tenantNId,
            environmentNId,
            serviceKey,
            databaseIdentityFingerprint,
            observedVersion,
            artifactChecksum,
            observedOn,
            operationNId,
            verificationStatus);
}
