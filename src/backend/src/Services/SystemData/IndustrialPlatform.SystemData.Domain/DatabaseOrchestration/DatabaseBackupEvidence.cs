using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

/// <summary>
/// 计划备份证据聚合根(05 方案 §8.1 <c>system_data_database_backup_evidence</c>)。
/// 创建为 Captured,验证后转 Verified;不得保存备份访问 Secret/凭据,只保留引用与时间。
/// </summary>
public sealed class DatabaseBackupEvidence : AggregateRoot
{
    /// <summary>BackupProvider 最大长度。</summary>
    public const int ProviderMaxLength = 64;

    /// <summary>BackupReference 最大长度。</summary>
    public const int ReferenceMaxLength = 256;

    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; private set; }

    /// <summary>备份证据业务标识。</summary>
    public string EvidenceNId { get; private set; }

    /// <summary>关联计划业务标识。</summary>
    public string PlanNId { get; private set; }

    /// <summary>关联计划校验和快照。</summary>
    public string PlanChecksum { get; private set; }

    /// <summary>关联计划目标状态指纹快照。</summary>
    public string TargetStateFingerprint { get; private set; }

    /// <summary>备份提供程序标识。</summary>
    public string BackupProvider { get; private set; }

    /// <summary>备份引用(如存储路径/快照 id,非访问凭据)。</summary>
    public string BackupReference { get; private set; }

    /// <summary>备份完成(捕获)时间。</summary>
    public DateTimeOffset CapturedOn { get; private set; }

    /// <summary>验证通过时间(验证后非空)。</summary>
    public DateTimeOffset? VerifiedOn { get; private set; }

    /// <summary>备份保留期截止。</summary>
    public DateTimeOffset RetentionUntil { get; private set; }

    /// <summary>验证人业务标识。</summary>
    public string? VerifiedByUserNId { get; private set; }

    /// <summary>备份证据状态。</summary>
    public BackupEvidenceStatus Status { get; private set; }

    private DatabaseBackupEvidence()
    {
        TenantNId = string.Empty;
        EvidenceNId = string.Empty;
        PlanNId = string.Empty;
        PlanChecksum = string.Empty;
        TargetStateFingerprint = string.Empty;
        BackupProvider = string.Empty;
        BackupReference = string.Empty;
    }

    private DatabaseBackupEvidence(
        string tenantNId,
        string evidenceNId,
        string planNId,
        string planChecksum,
        string targetStateFingerprint,
        string backupProvider,
        string backupReference,
        DateTimeOffset capturedOn,
        DateTimeOffset retentionUntil)
    {
        TenantNId = DatabaseOrchestrationGuard.RequireNId(tenantNId, "备份证据的租户标识不能为空。");
        EvidenceNId = DatabaseOrchestrationGuard.RequireNId(evidenceNId, "备份证据标识不能为空。");
        PlanNId = DatabaseOrchestrationGuard.RequireNId(planNId, "关联计划标识不能为空。");
        PlanChecksum = DatabaseOrchestrationGuard.RequireSha256Hex(planChecksum, "计划校验和不能为空。");
        TargetStateFingerprint = DatabaseOrchestrationGuard.RequireSha256Hex(targetStateFingerprint, "目标状态指纹不能为空。");
        BackupProvider = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            backupProvider, "备份提供程序不能为空。", ProviderMaxLength, $"备份提供程序标识长度不能超过 {ProviderMaxLength} 个字符。");
        BackupReference = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            backupReference, "备份引用不能为空。", ReferenceMaxLength, $"备份引用长度不能超过 {ReferenceMaxLength} 个字符。");
        if (retentionUntil <= capturedOn)
        {
            throw new ValidationException("备份保留期截止必须晚于捕获时间。");
        }

        CapturedOn = capturedOn;
        RetentionUntil = retentionUntil;
        Status = BackupEvidenceStatus.Captured;
    }

    /// <summary>持久化层重建专用构造,不重新校验。</summary>
    internal DatabaseBackupEvidence(
        Guid id,
        string tenantNId,
        string evidenceNId,
        string planNId,
        string planChecksum,
        string targetStateFingerprint,
        string backupProvider,
        string backupReference,
        DateTimeOffset capturedOn,
        DateTimeOffset? verifiedOn,
        DateTimeOffset retentionUntil,
        string? verifiedByUserNId,
        BackupEvidenceStatus status,
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
        EvidenceNId = evidenceNId;
        PlanNId = planNId;
        PlanChecksum = planChecksum;
        TargetStateFingerprint = targetStateFingerprint;
        BackupProvider = backupProvider;
        BackupReference = backupReference;
        CapturedOn = capturedOn;
        VerifiedOn = verifiedOn;
        RetentionUntil = retentionUntil;
        VerifiedByUserNId = verifiedByUserNId;
        Status = status;
        IsFrozen = isFrozen;
        IsLocked = isLocked;
        IsDeleted = isDeleted;
        EntityType = entityType;
        CreatedOn = createdOn;
        LastUpdatedOn = lastUpdatedOn;
        OptimisticVersion = optimisticVersion;
        ConcurrencyVersion = concurrencyVersion;
    }

    /// <summary>创建备份证据(Status = Captured)。</summary>
    public static DatabaseBackupEvidence Create(
        string tenantNId,
        string evidenceNId,
        string planNId,
        string planChecksum,
        string targetStateFingerprint,
        string backupProvider,
        string backupReference,
        DateTimeOffset capturedOn,
        DateTimeOffset retentionUntil)
        => new(
            tenantNId,
            evidenceNId,
            planNId,
            planChecksum,
            targetStateFingerprint,
            backupProvider,
            backupReference,
            capturedOn,
            retentionUntil);

    /// <summary>验证备份证据:仅允许 Captured 状态;超过保留期抛 <see cref="ValidationException"/>;通过后转 Verified。</summary>
    public void Verify(string verifiedByUserNId, DateTimeOffset verifiedOn)
    {
        EnsureCanModify();
        if (Status != BackupEvidenceStatus.Captured)
        {
            throw new ValidationException("只有 Captured 状态的备份证据才能验证。");
        }

        if (verifiedOn > RetentionUntil)
        {
            throw new ValidationException("备份证据已超过保留期,不能验证。");
        }

        VerifiedByUserNId = DatabaseOrchestrationGuard.RequireNId(verifiedByUserNId, "验证人标识不能为空。");
        VerifiedOn = verifiedOn;
        Status = BackupEvidenceStatus.Verified;
        Touch();
    }

    /// <summary>备份证据是否对给定计划在当前时刻有效:Verified + 未过保留期 + 校验和/指纹与计划一致。</summary>
    public bool IsValidFor(string planChecksum, string targetStateFingerprint, DateTimeOffset now) =>
        Status == BackupEvidenceStatus.Verified
        && now <= RetentionUntil
        && string.Equals(PlanChecksum, planChecksum, StringComparison.Ordinal)
        && string.Equals(TargetStateFingerprint, targetStateFingerprint, StringComparison.Ordinal);
}
