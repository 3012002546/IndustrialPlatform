using System.Text.Json;
using IndustrialPlatform.SystemData.Domain.Assignments;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.Organizations;
using IndustrialPlatform.SystemData.Domain.Positions;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence;

/// <summary>
/// POCO ↔ 聚合双向映射助手,集中承载 snake_case 物理列与领域类型的转换。
/// 持久化层专用(internal);领域不变量仍由聚合构造与业务方法维护,重建构造不重新校验。
/// 父子聚合(Plan/Operation)的子步骤由仓储单独装载/插入,不在本映射内。
/// </summary>
internal static class TableMapper
{
    // ===== 环境策略 =====

    public static DatabaseEnvironmentPolicyTable ToTable(DatabaseEnvironmentPolicy policy) => new()
    {
        Id = policy.Id,
        IsFrozen = policy.IsFrozen,
        IsLocked = policy.IsLocked,
        IsDeleted = policy.IsDeleted,
        EntityType = policy.EntityType,
        CreatedOn = policy.CreatedOn,
        LastUpdatedOn = policy.LastUpdatedOn,
        OptimisticVersion = policy.OptimisticVersion,
        ConcurrencyVersion = policy.ConcurrencyVersion,
        TenantNId = policy.TenantNId,
        EnvironmentNId = policy.EnvironmentNId,
        EnvironmentKind = policy.EnvironmentKind,
        ApprovalRequired = policy.ApprovalRequired,
        BackupRequired = policy.BackupRequired,
        PlanTtlSeconds = policy.PlanTtlSeconds,
        PlanTimeoutSeconds = policy.PlanTimeoutSeconds,
        ApplyTimeoutSeconds = policy.ApplyTimeoutSeconds,
        MaxPreMigrationRetries = policy.MaxPreMigrationRetries,
        PolicyRevision = policy.PolicyRevision,
    };

    public static DatabaseEnvironmentPolicy ToEnvironmentPolicy(DatabaseEnvironmentPolicyTable row) => new(
        row.Id,
        row.TenantNId,
        row.EnvironmentNId,
        row.EnvironmentKind,
        row.ApprovalRequired,
        row.BackupRequired,
        row.PlanTtlSeconds,
        row.PlanTimeoutSeconds,
        row.ApplyTimeoutSeconds,
        row.MaxPreMigrationRetries,
        row.PolicyRevision,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    // ===== 注册清单 =====

    public static DatabaseRegistrationTable ToTable(DatabaseRegistration registration) => new()
    {
        Id = registration.Id,
        IsFrozen = registration.IsFrozen,
        IsLocked = registration.IsLocked,
        IsDeleted = registration.IsDeleted,
        EntityType = registration.EntityType,
        CreatedOn = registration.CreatedOn,
        LastUpdatedOn = registration.LastUpdatedOn,
        OptimisticVersion = registration.OptimisticVersion,
        ConcurrencyVersion = registration.ConcurrencyVersion,
        TenantNId = registration.TenantNId,
        EnvironmentNId = registration.EnvironmentNId,
        ServiceKey = registration.ServiceKey,
        ModuleKey = registration.ModuleKey,
        SeedSets = SerializeSeedSets(registration.SeedSets),
        Provider = registration.Provider,
        LogicalDatabaseName = registration.LogicalDatabaseName,
        PhysicalDatabaseName = registration.PhysicalDatabaseName,
        IsSharedPhysicalDatabase = registration.IsSharedPhysicalDatabase,
        TopologyMode = registration.TopologyMode,
        TopologyRevision = registration.TopologyRevision,
        MigrationArtifactId = registration.MigrationArtifactId,
        MigrationVersion = registration.MigrationVersion,
        ArtifactChecksum = registration.ArtifactChecksum,
        ArtifactSignature = registration.ArtifactSignature,
        OwnerNId = registration.OwnerNId,
        DesiredState = registration.DesiredState,
        AutoProvision = registration.AutoProvision,
        AutoMigrate = registration.AutoMigrate,
        ManifestVersion = registration.ManifestVersion,
        ManifestChecksum = registration.ManifestChecksum,
        Status = registration.Status,
    };

    public static DatabaseRegistration ToRegistration(DatabaseRegistrationTable row) => new(
        row.Id,
        row.TenantNId,
        row.EnvironmentNId,
        row.ServiceKey,
        row.ModuleKey,
        row.Provider,
        row.LogicalDatabaseName,
        row.PhysicalDatabaseName,
        row.IsSharedPhysicalDatabase,
        row.TopologyMode,
        row.TopologyRevision,
        row.MigrationArtifactId,
        row.MigrationVersion,
        row.ArtifactChecksum,
        row.ArtifactSignature,
        row.OwnerNId,
        row.DesiredState,
        row.AutoProvision,
        row.AutoMigrate,
        row.ManifestVersion,
        row.ManifestChecksum,
        DeserializeSeedSets(row.SeedSets),
        row.Status,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    // ===== 种子声明 JSON 序列化 =====

    /// <summary>种子声明集合 → JSON 文本(枚举以名字符串存储;空集合存空串)。</summary>
    private static string SerializeSeedSets(IReadOnlyCollection<SeedSet> seedSets) =>
        seedSets.Count == 0
            ? string.Empty
            : JsonSerializer.Serialize(seedSets.Select(ToSeedSetDto).ToList());

    /// <summary>JSON 文本 → 种子声明集合(重建构造不重新校验)。</summary>
    private static List<SeedSet> DeserializeSeedSets(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var dtos = JsonSerializer.Deserialize<List<SeedSetPersistenceDto>>(json);
        if (dtos is null || dtos.Count == 0)
        {
            return [];
        }

        return dtos.Select(ToSeedSet).ToList();
    }

    private static SeedSetPersistenceDto ToSeedSetDto(SeedSet seed) => new()
    {
        SeedKey = seed.SeedKey,
        SeedVersion = seed.SeedVersion,
        SeedClass = seed.SeedClass.ToString(),
        Scope = seed.Scope.ToString(),
        SeedArtifactId = seed.SeedArtifactId,
        SeedChecksum = seed.SeedChecksum,
        SeedSignature = seed.SeedSignature,
        RequiredForReadiness = seed.RequiredForReadiness,
        AllowedEnvironments = seed.AllowedEnvironments,
        DependsOnMigrationVersion = seed.DependsOnMigrationVersion,
        DependsOnSeedKeys = seed.DependsOnSeedKeys,
        BootstrapPolicy = seed.BootstrapPolicy.ToString(),
    };

    private static SeedSet ToSeedSet(SeedSetPersistenceDto dto) => new(
        dto.SeedKey,
        dto.SeedVersion,
        ParseEnum<SeedClass>(dto.SeedClass),
        ParseEnum<SeedScope>(dto.Scope),
        dto.SeedArtifactId,
        dto.SeedChecksum,
        dto.SeedSignature,
        dto.RequiredForReadiness,
        dto.AllowedEnvironments,
        dto.DependsOnMigrationVersion,
        dto.DependsOnSeedKeys,
        ParseEnum<BootstrapPolicy>(dto.BootstrapPolicy),
        skipValidation: true);

    private static T ParseEnum<T>(string value)
        where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : default;

    /// <summary>种子声明的持久化中间模型(公开 get/set 供 STJ 序列化)。</summary>
    private sealed class SeedSetPersistenceDto
    {
        public string SeedKey { get; set; } = string.Empty;

        public string SeedVersion { get; set; } = string.Empty;

        public string SeedClass { get; set; } = string.Empty;

        public string Scope { get; set; } = string.Empty;

        public string SeedArtifactId { get; set; } = string.Empty;

        public string SeedChecksum { get; set; } = string.Empty;

        public string? SeedSignature { get; set; }

        public bool RequiredForReadiness { get; set; }

        public string AllowedEnvironments { get; set; } = string.Empty;

        public string? DependsOnMigrationVersion { get; set; }

        public string? DependsOnSeedKeys { get; set; }

        public string BootstrapPolicy { get; set; } = string.Empty;
    }

    // ===== 不可变计划 =====

    public static DatabaseProvisionPlanTable ToTable(DatabaseProvisionPlan plan) => new()
    {
        Id = plan.Id,
        IsFrozen = plan.IsFrozen,
        IsLocked = plan.IsLocked,
        IsDeleted = plan.IsDeleted,
        EntityType = plan.EntityType,
        CreatedOn = plan.CreatedOn,
        LastUpdatedOn = plan.LastUpdatedOn,
        OptimisticVersion = plan.OptimisticVersion,
        ConcurrencyVersion = plan.ConcurrencyVersion,
        TenantNId = plan.TenantNId,
        PlanNId = plan.PlanNId,
        EnvironmentNId = plan.EnvironmentNId,
        ServiceKey = plan.ServiceKey,
        ModuleKey = plan.ModuleKey,
        RequestedMigrationVersion = plan.RequestedMigrationVersion,
        CurrentMigrationVersion = plan.CurrentMigrationVersion,
        TargetStateFingerprint = plan.TargetStateFingerprint,
        PlanChecksum = plan.PlanChecksum,
        RiskLevel = plan.RiskLevel,
        DestructiveChangeDetected = plan.DestructiveChangeDetected,
        RequiredPolicies = plan.RequiredPolicies,
        ExpiresOn = plan.ExpiresOn,
        CreatedByUserNId = plan.CreatedByUserNId,
    };

    public static DatabaseProvisionPlan ToPlan(
        DatabaseProvisionPlanTable row,
        IReadOnlyCollection<DatabasePlanStep> steps) => new(
        row.Id,
        row.TenantNId,
        row.PlanNId,
        row.EnvironmentNId,
        row.ServiceKey,
        row.ModuleKey,
        row.RequestedMigrationVersion,
        row.CurrentMigrationVersion,
        row.TargetStateFingerprint,
        row.PlanChecksum,
        row.RiskLevel,
        row.DestructiveChangeDetected,
        row.RequiredPolicies,
        row.ExpiresOn,
        row.CreatedByUserNId,
        steps,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    public static DatabasePlanStepTable ToTable(DatabasePlanStep step, Guid planId, bool planIsDeleted) => new()
    {
        Id = step.Id,
        IsFrozen = step.IsFrozen,
        IsLocked = step.IsLocked,
        IsDeleted = step.IsDeleted,
        EntityType = step.EntityType,
        CreatedOn = step.CreatedOn,
        LastUpdatedOn = step.LastUpdatedOn,
        OptimisticVersion = step.OptimisticVersion,
        ConcurrencyVersion = step.ConcurrencyVersion,
        PlanId = planId,
        PlanIsDeleted = planIsDeleted,
        Sequence = step.Sequence,
        StepKind = step.StepKind,
        InputSummary = step.InputSummary,
        PreconditionSummary = step.PreconditionSummary,
        PostconditionSummary = step.PostconditionSummary,
        RiskLevel = step.RiskLevel,
    };

    public static DatabasePlanStep ToPlanStep(DatabasePlanStepTable row) => new(
        row.Id,
        row.Sequence,
        row.StepKind,
        row.InputSummary,
        row.PreconditionSummary,
        row.PostconditionSummary,
        row.RiskLevel,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    // ===== 审批 =====

    public static DatabaseApprovalTable ToTable(DatabaseApproval approval) => new()
    {
        Id = approval.Id,
        IsFrozen = approval.IsFrozen,
        IsLocked = approval.IsLocked,
        IsDeleted = approval.IsDeleted,
        EntityType = approval.EntityType,
        CreatedOn = approval.CreatedOn,
        LastUpdatedOn = approval.LastUpdatedOn,
        OptimisticVersion = approval.OptimisticVersion,
        ConcurrencyVersion = approval.ConcurrencyVersion,
        TenantNId = approval.TenantNId,
        ApprovalNId = approval.ApprovalNId,
        PlanNId = approval.PlanNId,
        PlanChecksum = approval.PlanChecksum,
        TargetStateFingerprint = approval.TargetStateFingerprint,
        ApprovedByUserNId = approval.ApprovedByUserNId,
        Reason = approval.Reason,
        ApprovedOn = approval.ApprovedOn,
        ExpiresOn = approval.ExpiresOn,
        Status = approval.Status,
    };

    public static DatabaseApproval ToApproval(DatabaseApprovalTable row) => new(
        row.Id,
        row.TenantNId,
        row.ApprovalNId,
        row.PlanNId,
        row.PlanChecksum,
        row.TargetStateFingerprint,
        row.ApprovedByUserNId,
        row.Reason,
        row.ApprovedOn,
        row.ExpiresOn,
        row.Status,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    // ===== 备份证据 =====

    public static DatabaseBackupEvidenceTable ToTable(DatabaseBackupEvidence evidence) => new()
    {
        Id = evidence.Id,
        IsFrozen = evidence.IsFrozen,
        IsLocked = evidence.IsLocked,
        IsDeleted = evidence.IsDeleted,
        EntityType = evidence.EntityType,
        CreatedOn = evidence.CreatedOn,
        LastUpdatedOn = evidence.LastUpdatedOn,
        OptimisticVersion = evidence.OptimisticVersion,
        ConcurrencyVersion = evidence.ConcurrencyVersion,
        TenantNId = evidence.TenantNId,
        EvidenceNId = evidence.EvidenceNId,
        PlanNId = evidence.PlanNId,
        PlanChecksum = evidence.PlanChecksum,
        TargetStateFingerprint = evidence.TargetStateFingerprint,
        BackupProvider = evidence.BackupProvider,
        BackupReference = evidence.BackupReference,
        CapturedOn = evidence.CapturedOn,
        VerifiedOn = evidence.VerifiedOn,
        RetentionUntil = evidence.RetentionUntil,
        VerifiedByUserNId = evidence.VerifiedByUserNId,
        Status = evidence.Status,
    };

    public static DatabaseBackupEvidence ToBackupEvidence(DatabaseBackupEvidenceTable row) => new(
        row.Id,
        row.TenantNId,
        row.EvidenceNId,
        row.PlanNId,
        row.PlanChecksum,
        row.TargetStateFingerprint,
        row.BackupProvider,
        row.BackupReference,
        row.CapturedOn,
        row.VerifiedOn,
        row.RetentionUntil,
        row.VerifiedByUserNId,
        row.Status,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    // ===== Operation =====

    public static DatabaseProvisionOperationTable ToTable(DatabaseProvisionOperation operation) => new()
    {
        Id = operation.Id,
        IsFrozen = operation.IsFrozen,
        IsLocked = operation.IsLocked,
        IsDeleted = operation.IsDeleted,
        EntityType = operation.EntityType,
        CreatedOn = operation.CreatedOn,
        LastUpdatedOn = operation.LastUpdatedOn,
        OptimisticVersion = operation.OptimisticVersion,
        ConcurrencyVersion = operation.ConcurrencyVersion,
        TenantNId = operation.TenantNId,
        OperationNId = operation.OperationNId,
        Kind = operation.Kind,
        EnvironmentNId = operation.EnvironmentNId,
        ServiceKey = operation.ServiceKey,
        ModuleKey = operation.ModuleKey,
        PlanNId = operation.PlanNId,
        RequestedVersion = operation.RequestedVersion,
        IdempotencyKey = operation.IdempotencyKey,
        RequestHash = operation.RequestHash,
        Status = operation.Status,
        Phase = operation.Phase,
        Attempt = operation.Attempt,
        LeaseOwner = operation.LeaseOwner,
        LeaseExpiresOn = operation.LeaseExpiresOn,
        HeartbeatOn = operation.HeartbeatOn,
        QueuedOn = operation.QueuedOn,
        StartedOn = operation.StartedOn,
        CompletedOn = operation.CompletedOn,
        TimeoutOn = operation.TimeoutOn,
        SanitizedErrorCode = operation.SanitizedErrorCode,
        SanitizedErrorSummary = operation.SanitizedErrorSummary,
        TraceId = operation.TraceId,
        CreatedByUserNId = operation.CreatedByUserNId,
    };

    public static DatabaseProvisionOperation ToOperation(
        DatabaseProvisionOperationTable row,
        IReadOnlyCollection<DatabaseOperationStep> steps) => new(
        row.Id,
        row.TenantNId,
        row.OperationNId,
        row.Kind,
        row.EnvironmentNId,
        row.ServiceKey,
        row.ModuleKey,
        row.PlanNId,
        row.RequestedVersion,
        row.IdempotencyKey,
        row.RequestHash,
        row.Status,
        row.Phase,
        row.Attempt,
        row.LeaseOwner,
        row.LeaseExpiresOn,
        row.HeartbeatOn,
        row.QueuedOn,
        row.StartedOn,
        row.CompletedOn,
        row.TimeoutOn,
        row.SanitizedErrorCode,
        row.SanitizedErrorSummary,
        row.TraceId,
        row.CreatedByUserNId,
        steps,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    public static DatabaseOperationStepTable ToTable(DatabaseOperationStep step, Guid operationId, bool operationIsDeleted) => new()
    {
        Id = step.Id,
        IsFrozen = step.IsFrozen,
        IsLocked = step.IsLocked,
        IsDeleted = step.IsDeleted,
        EntityType = step.EntityType,
        CreatedOn = step.CreatedOn,
        LastUpdatedOn = step.LastUpdatedOn,
        OptimisticVersion = step.OptimisticVersion,
        ConcurrencyVersion = step.ConcurrencyVersion,
        OperationId = operationId,
        OperationIsDeleted = operationIsDeleted,
        Sequence = step.Sequence,
        Phase = step.Phase,
        Attempt = step.Attempt,
        Status = step.Status,
        StartedOn = step.StartedOn,
        CompletedOn = step.CompletedOn,
        SanitizedErrorCode = step.SanitizedErrorCode,
        SanitizedErrorSummary = step.SanitizedErrorSummary,
    };

    public static DatabaseOperationStep ToOperationStep(DatabaseOperationStepTable row) => new(
        row.Id,
        row.Sequence,
        row.Phase,
        row.Attempt,
        row.Status,
        row.StartedOn,
        row.CompletedOn,
        row.SanitizedErrorCode,
        row.SanitizedErrorSummary,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    // ===== 迁移观察 =====

    public static DatabaseMigrationObservationTable ToTable(DatabaseMigrationObservation observation) => new()
    {
        Id = observation.Id,
        IsFrozen = observation.IsFrozen,
        IsLocked = observation.IsLocked,
        IsDeleted = observation.IsDeleted,
        EntityType = observation.EntityType,
        CreatedOn = observation.CreatedOn,
        LastUpdatedOn = observation.LastUpdatedOn,
        OptimisticVersion = observation.OptimisticVersion,
        ConcurrencyVersion = observation.ConcurrencyVersion,
        TenantNId = observation.TenantNId,
        EnvironmentNId = observation.EnvironmentNId,
        ServiceKey = observation.ServiceKey,
        DatabaseIdentityFingerprint = observation.DatabaseIdentityFingerprint,
        ObservedVersion = observation.ObservedVersion,
        ArtifactChecksum = observation.ArtifactChecksum,
        ObservedOn = observation.ObservedOn,
        OperationNId = observation.OperationNId,
        VerificationStatus = observation.VerificationStatus,
    };

    public static DatabaseMigrationObservation ToObservation(DatabaseMigrationObservationTable row) => new(
        row.Id,
        row.TenantNId,
        row.EnvironmentNId,
        row.ServiceKey,
        row.DatabaseIdentityFingerprint,
        row.ObservedVersion,
        row.ArtifactChecksum,
        row.ObservedOn,
        row.OperationNId,
        row.VerificationStatus,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    // ===== 种子观察 =====

    public static DatabaseSeedObservationTable ToTable(DatabaseSeedObservation observation) => new()
    {
        Id = observation.Id,
        IsFrozen = observation.IsFrozen,
        IsLocked = observation.IsLocked,
        IsDeleted = observation.IsDeleted,
        EntityType = observation.EntityType,
        CreatedOn = observation.CreatedOn,
        LastUpdatedOn = observation.LastUpdatedOn,
        OptimisticVersion = observation.OptimisticVersion,
        ConcurrencyVersion = observation.ConcurrencyVersion,
        TenantNId = observation.TenantNId,
        EnvironmentNId = observation.EnvironmentNId,
        ServiceKey = observation.ServiceKey,
        ModuleKey = observation.ModuleKey,
        SeedKey = observation.SeedKey,
        SeedVersion = observation.SeedVersion,
        Checksum = observation.Checksum,
        Scope = observation.Scope,
        Status = observation.Status,
        AppliedOn = observation.AppliedOn,
        OperationNId = observation.OperationNId,
        VerificationStatus = observation.VerificationStatus,
    };

    public static DatabaseSeedObservation ToObservation(DatabaseSeedObservationTable row) => new(
        row.Id,
        row.TenantNId,
        row.EnvironmentNId,
        row.ServiceKey,
        row.ModuleKey,
        row.SeedKey,
        row.SeedVersion,
        row.Checksum,
        row.Scope,
        row.Status,
        row.AppliedOn,
        row.OperationNId,
        row.VerificationStatus,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    // ===== 行政组织 =====

    public static AdministrativeOrganizationTable ToTable(AdministrativeOrganization organization) => new()
    {
        Id = organization.Id,
        IsFrozen = organization.IsFrozen,
        IsLocked = organization.IsLocked,
        IsDeleted = organization.IsDeleted,
        EntityType = organization.EntityType,
        CreatedOn = organization.CreatedOn,
        LastUpdatedOn = organization.LastUpdatedOn,
        OptimisticVersion = organization.OptimisticVersion,
        ConcurrencyVersion = organization.ConcurrencyVersion,
        TenantNId = organization.TenantNId,
        NId = organization.NId,
        NormalizedNId = organization.NormalizedNId,
        Name = organization.Name,
        NormalizedName = organization.Name.Trim().ToUpperInvariant(),
        Type = (int)organization.Type,
        ParentOrganizationNId = organization.ParentOrganizationNId,
        ParentOrganizationId = organization.ParentOrganizationId,
        ParentOrganizationIsDeleted = organization.ParentOrganizationIsDeleted,
        DisplayOrder = organization.DisplayOrder,
        Status = (int)organization.Status,
        OrganizationRevision = organization.OrganizationRevision,
    };

    public static AdministrativeOrganization ToAdministrativeOrganization(AdministrativeOrganizationTable row) => new(
        row.Id,
        row.TenantNId,
        row.NId,
        row.NormalizedNId,
        row.Name,
        (AdministrativeOrganizationType)row.Type,
        row.ParentOrganizationNId,
        row.ParentOrganizationId,
        row.ParentOrganizationIsDeleted,
        row.DisplayOrder,
        (OrganizationStatus)row.Status,
        row.OrganizationRevision,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    // ===== 岗位 =====

    public static PositionTable ToTable(Position position) => new()
    {
        Id = position.Id,
        IsFrozen = position.IsFrozen,
        IsLocked = position.IsLocked,
        IsDeleted = position.IsDeleted,
        EntityType = position.EntityType,
        CreatedOn = position.CreatedOn,
        LastUpdatedOn = position.LastUpdatedOn,
        OptimisticVersion = position.OptimisticVersion,
        ConcurrencyVersion = position.ConcurrencyVersion,
        TenantNId = position.TenantNId,
        NId = position.NId,
        NormalizedNId = position.NormalizedNId,
        OrganizationNId = position.OrganizationNId,
        OrganizationId = position.OrganizationId,
        OrganizationIsDeleted = position.OrganizationIsDeleted,
        Name = position.Name,
        NormalizedName = position.Name.Trim().ToUpperInvariant(),
        Description = position.Description,
        DisplayOrder = position.DisplayOrder,
        Status = (int)position.Status,
    };

    public static Position ToPosition(PositionTable row) => new(
        row.Id,
        row.TenantNId,
        row.NId,
        row.NormalizedNId,
        row.OrganizationNId,
        row.OrganizationId,
        row.OrganizationIsDeleted,
        row.Name,
        row.Description,
        row.DisplayOrder,
        (PositionStatus)row.Status,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    // ===== 用户任职 =====

    public static UserAssignmentTable ToTable(UserAssignment assignment) => new()
    {
        Id = assignment.Id,
        IsFrozen = assignment.IsFrozen,
        IsLocked = assignment.IsLocked,
        IsDeleted = assignment.IsDeleted,
        EntityType = assignment.EntityType,
        CreatedOn = assignment.CreatedOn,
        LastUpdatedOn = assignment.LastUpdatedOn,
        OptimisticVersion = assignment.OptimisticVersion,
        ConcurrencyVersion = assignment.ConcurrencyVersion,
        TenantNId = assignment.TenantNId,
        NId = assignment.NId,
        NormalizedNId = assignment.NormalizedNId,
        UserNId = assignment.UserNId,
        UserDisplayNameSnapshot = assignment.UserDisplayNameSnapshot,
        OrganizationNId = assignment.OrganizationNId,
        PositionNId = assignment.PositionNId,
        PositionId = assignment.PositionId,
        PositionIsDeleted = assignment.PositionIsDeleted,
        IsPrimary = assignment.IsPrimary,
        EffectiveFrom = assignment.EffectiveFrom,
        EffectiveTo = assignment.EffectiveTo,
        State = (int)assignment.State,
        CancelledOn = assignment.CancelledOn,
        CancelReason = assignment.CancelReason,
    };

    public static UserAssignment ToUserAssignment(UserAssignmentTable row) => new(
        row.Id,
        row.TenantNId,
        row.NId,
        row.NormalizedNId,
        row.UserNId,
        row.UserDisplayNameSnapshot,
        row.OrganizationNId,
        row.PositionNId,
        row.PositionId,
        row.PositionIsDeleted,
        row.IsPrimary,
        row.EffectiveFrom,
        row.EffectiveTo,
        (AssignmentState)row.State,
        row.CancelledOn,
        row.CancelReason,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);
}
