namespace IndustrialPlatform.SystemData.Contracts.DatabaseOrchestration;

// =====================================================================
// 数据库编排公开契约(TASK-SD-002,冻结不含 Secret 的 API 形状)。
// 约定:
//  - 请求属性一律 string?/可空枚举(防 [ApiController] Required 推断破坏统一信封);
//  - 枚举以枚举名字符串传输(如 "SourceOfTruth"),不依赖 JSON 数字;
//  - 不暴露数据库地址、角色密码、SecretRef、SQL 或原始迁移输出;
//  - 环境(NId)由服务端可信拓扑解析,请求不含环境标识(防客户端伪造)。
// =====================================================================

/// <summary>注册清单请求(PUT /registrations/{serviceKey})。</summary>
public sealed record DatabaseRegistrationManifestV1
{
    /// <summary>服务稳定键。</summary>
    public string? ServiceKey { get; init; }

    /// <summary>稳定逻辑库名。</summary>
    public string? LogicalDatabaseName { get; init; }

    /// <summary>数据库提供程序标识(<c>Sqlite</c>/<c>PostgreSQL</c>),缺省按拓扑解析。</summary>
    public string? Provider { get; init; }

    /// <summary>拓扑模式(<c>Shared</c>/<c>PerService</c>),缺省按拓扑解析。</summary>
    public string? TopologyMode { get; init; }

    /// <summary>迁移产物标识。</summary>
    public string? MigrationArtifactId { get; init; }

    /// <summary>请求的目标迁移版本。</summary>
    public string? RequestedVersion { get; init; }

    /// <summary>迁移产物校验和(SHA-256 十六进制)。</summary>
    public string? ArtifactChecksum { get; init; }

    /// <summary>产物签名引用(仅非敏感引用,绝不发送私钥/凭据)。</summary>
    public string? ArtifactSignature { get; init; }

    /// <summary>期望数据状态枚举名(缺省 <c>SourceOfTruth</c>)。</summary>
    public string? DesiredState { get; init; }

    /// <summary>是否允许自动 provision,缺省 false。</summary>
    public bool? AutoProvision { get; init; }

    /// <summary>是否允许自动迁移,缺省 false。</summary>
    public bool? AutoMigrate { get; init; }

    /// <summary>所有者业务标识,缺省为当前操作人。</summary>
    public string? OwnerNId { get; init; }

    /// <summary>清单版本,缺省 <c>1</c>。</summary>
    public string? ManifestVersion { get; init; }
}

/// <summary>计划请求(POST /plans,入队异步计划)。</summary>
public sealed record DatabasePlanRequestV1
{
    /// <summary>服务稳定键。</summary>
    public string? ServiceKey { get; init; }

    /// <summary>请求的目标迁移版本。</summary>
    public string? RequestedVersion { get; init; }

    /// <summary>期望数据状态枚举名(缺省沿用注册清单)。</summary>
    public string? DesiredState { get; init; }
}

/// <summary>apply 请求(POST /operations/apply,入队异步 apply)。</summary>
public sealed record DatabaseApplyRequestV1
{
    /// <summary>已生成计划业务标识。</summary>
    public string? PlanNId { get; init; }

    /// <summary>请求的目标迁移版本(幂等哈希输入,须与计划一致)。</summary>
    public string? RequestedVersion { get; init; }
}

/// <summary>审批请求(POST /plans/{planNId}/approvals)。</summary>
public sealed record DatabaseApprovalRequestV1
{
    /// <summary>审批理由(可选)。</summary>
    public string? Reason { get; init; }
}

/// <summary>备份证据请求(POST /plans/{planNId}/backup-evidence)。</summary>
public sealed record DatabaseBackupEvidenceRequestV1
{
    /// <summary>备份提供程序标识。</summary>
    public string? BackupProvider { get; init; }

    /// <summary>备份引用(如存储路径/快照 id,非访问凭据)。</summary>
    public string? BackupReference { get; init; }

    /// <summary>备份保留期截止(缺省按环境策略/默认 7 天)。</summary>
    public DateTimeOffset? RetentionUntil { get; init; }
}

/// <summary>注册清单响应。</summary>
public sealed record DatabaseRegistrationV1
{
    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; init; } = string.Empty;

    /// <summary>环境业务标识。</summary>
    public string EnvironmentNId { get; init; } = string.Empty;

    /// <summary>服务稳定键。</summary>
    public string ServiceKey { get; init; } = string.Empty;

    /// <summary>模块标识(v2;migration-only v1 为 ServiceKey 兼容值)。</summary>
    public string ModuleKey { get; init; } = string.Empty;

    /// <summary>是否由服务初始化器拥有生命周期;false 表示 legacy SQL Runner。</summary>
    public bool UsesServiceInitializer { get; init; }

    /// <summary>数据库提供程序标识。</summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>稳定逻辑库名。</summary>
    public string LogicalDatabaseName { get; init; } = string.Empty;

    /// <summary>解析出的物理库名。</summary>
    public string PhysicalDatabaseName { get; init; } = string.Empty;

    /// <summary>脱敏后的物理库目标;V2 管理端应优先展示此字段。</summary>
    public string PhysicalDatabaseTarget { get; init; } = string.Empty;

    /// <summary>是否与其他服务共享物理数据库。</summary>
    public bool IsSharedPhysicalDatabase { get; init; }

    /// <summary>拓扑模式。</summary>
    public string TopologyMode { get; init; } = string.Empty;

    /// <summary>拓扑 revision。</summary>
    public string TopologyRevision { get; init; } = string.Empty;

    /// <summary>迁移产物标识。</summary>
    public string MigrationArtifactId { get; init; } = string.Empty;

    /// <summary>迁移版本。</summary>
    public string MigrationVersion { get; init; } = string.Empty;

    /// <summary>产物校验和。</summary>
    public string ArtifactChecksum { get; init; } = string.Empty;

    /// <summary>产物签名引用(非 Secret)。</summary>
    public string? ArtifactSignature { get; init; }

    /// <summary>所有者业务标识。</summary>
    public string OwnerNId { get; init; } = string.Empty;

    /// <summary>期望数据状态枚举名。</summary>
    public string DesiredState { get; init; } = string.Empty;

    /// <summary>是否允许自动 provision。</summary>
    public bool AutoProvision { get; init; }

    /// <summary>是否允许自动迁移。</summary>
    public bool AutoMigrate { get; init; }

    /// <summary>清单版本。</summary>
    public string ManifestVersion { get; init; } = string.Empty;

    /// <summary>清单校验和(非 Secret)。</summary>
    public string ManifestChecksum { get; init; } = string.Empty;

    /// <summary>注册状态枚举名。</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>模块版本化种子声明(migration-only v1 为空)。</summary>
    public IReadOnlyCollection<SeedSetV1>? SeedSets { get; init; }

    /// <summary>注册时间。</summary>
    public DateTimeOffset RegisteredOn { get; init; }

    /// <summary>最近更新时间。</summary>
    public DateTimeOffset LastUpdatedOn { get; init; }
}

/// <summary>注册清单列表项。</summary>
public sealed record DatabaseRegistrationSummaryV1
{
    /// <summary>服务稳定键。</summary>
    public string ServiceKey { get; init; } = string.Empty;

    /// <summary>模块标识(v2;migration-only v1 为 ServiceKey 兼容值)。</summary>
    public string ModuleKey { get; init; } = string.Empty;

    /// <summary>是否由服务初始化器拥有生命周期。</summary>
    public bool UsesServiceInitializer { get; init; }

    /// <summary>稳定逻辑库名。</summary>
    public string LogicalDatabaseName { get; init; } = string.Empty;

    /// <summary>数据库提供程序标识。</summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>迁移版本。</summary>
    public string MigrationVersion { get; init; } = string.Empty;

    /// <summary>期望数据状态枚举名。</summary>
    public string DesiredState { get; init; } = string.Empty;

    /// <summary>注册状态枚举名。</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>拓扑 revision。</summary>
    public string TopologyRevision { get; init; } = string.Empty;

    /// <summary>注册时间。</summary>
    public DateTimeOffset RegisteredOn { get; init; }

    /// <summary>最近更新时间。</summary>
    public DateTimeOffset LastUpdatedOn { get; init; }
}

/// <summary>环境策略响应。</summary>
public sealed record EnvironmentPolicyV1
{
    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; init; } = string.Empty;

    /// <summary>环境业务标识。</summary>
    public string EnvironmentNId { get; init; } = string.Empty;

    /// <summary>环境种类枚举名。</summary>
    public string EnvironmentKind { get; init; } = string.Empty;

    /// <summary>是否要求人工审批。</summary>
    public bool ApprovalRequired { get; init; }

    /// <summary>是否要求备份证据。</summary>
    public bool BackupRequired { get; init; }

    /// <summary>计划 TTL(秒)。</summary>
    public int PlanTtlSeconds { get; init; }

    /// <summary>plan 操作超时(秒)。</summary>
    public int PlanTimeoutSeconds { get; init; }

    /// <summary>apply 操作超时(秒)。</summary>
    public int ApplyTimeoutSeconds { get; init; }

    /// <summary>预迁移最大重试次数。</summary>
    public int MaxPreMigrationRetries { get; init; }

    /// <summary>策略版本号。</summary>
    public int PolicyRevision { get; init; }

    /// <summary>服务初始化策略(<c>Standard</c>/<c>Advanced</c>),由服务端有效策略推导。</summary>
    public string InitializationPolicy { get; init; } = string.Empty;

    /// <summary>是否来自租户显式策略;false 表示可信环境默认值。</summary>
    public bool IsExplicit { get; init; }
}

/// <summary>不可变计划响应。</summary>
public sealed record DatabasePlanV1
{
    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; init; } = string.Empty;

    /// <summary>计划业务标识。</summary>
    public string PlanNId { get; init; } = string.Empty;

    /// <summary>环境业务标识。</summary>
    public string EnvironmentNId { get; init; } = string.Empty;

    /// <summary>服务稳定键。</summary>
    public string ServiceKey { get; init; } = string.Empty;

    /// <summary>模块标识(v2;migration-only v1 为 ServiceKey 兼容值)。</summary>
    public string ModuleKey { get; init; } = string.Empty;

    /// <summary>请求的目标迁移版本。</summary>
    public string RequestedMigrationVersion { get; init; } = string.Empty;

    /// <summary>计划生成时观察到的当前版本。</summary>
    public string CurrentMigrationVersion { get; init; } = string.Empty;

    /// <summary>目标状态指纹。</summary>
    public string TargetStateFingerprint { get; init; } = string.Empty;

    /// <summary>计划校验和。</summary>
    public string PlanChecksum { get; init; } = string.Empty;

    /// <summary>风险等级枚举名。</summary>
    public string RiskLevel { get; init; } = string.Empty;

    /// <summary>是否检测到破坏性变更。</summary>
    public bool DestructiveChangeDetected { get; init; }

    /// <summary>要求的环境门禁枚举名(可组合)。</summary>
    public string RequiredPolicies { get; init; } = string.Empty;

    /// <summary>计划有效期截止。</summary>
    public DateTimeOffset ExpiresOn { get; init; }

    /// <summary>是否已过期。</summary>
    public bool IsExpired { get; init; }

    /// <summary>创建人业务标识。</summary>
    public string CreatedByUserNId { get; init; } = string.Empty;

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedOn { get; init; }

    /// <summary>按顺序排列的计划步骤。</summary>
    public IReadOnlyCollection<DatabasePlanStepV1> Steps { get; init; } = [];
}

/// <summary>不可变计划步骤响应。</summary>
public sealed record DatabasePlanStepV1
{
    /// <summary>顺序(1 起)。</summary>
    public int Sequence { get; init; }

    /// <summary>稳定步骤标识。</summary>
    public string StepKind { get; init; } = string.Empty;

    /// <summary>风险等级枚举名。</summary>
    public string RiskLevel { get; init; } = string.Empty;

    /// <summary>输入摘要(不含敏感信息)。</summary>
    public string? InputSummary { get; init; }

    /// <summary>前置条件摘要。</summary>
    public string? PreconditionSummary { get; init; }

    /// <summary>后置条件摘要。</summary>
    public string? PostconditionSummary { get; init; }
}

/// <summary>审批记录响应。</summary>
public sealed record DatabaseApprovalV1
{
    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; init; } = string.Empty;

    /// <summary>审批记录业务标识。</summary>
    public string ApprovalNId { get; init; } = string.Empty;

    /// <summary>被审批计划业务标识。</summary>
    public string PlanNId { get; init; } = string.Empty;

    /// <summary>被审批计划校验和快照。</summary>
    public string PlanChecksum { get; init; } = string.Empty;

    /// <summary>被审批计划目标状态指纹快照。</summary>
    public string TargetStateFingerprint { get; init; } = string.Empty;

    /// <summary>审批人业务标识。</summary>
    public string ApprovedByUserNId { get; init; } = string.Empty;

    /// <summary>审批理由。</summary>
    public string? Reason { get; init; }

    /// <summary>审批时间。</summary>
    public DateTimeOffset ApprovedOn { get; init; }

    /// <summary>审批有效期截止。</summary>
    public DateTimeOffset ExpiresOn { get; init; }

    /// <summary>审批状态枚举名。</summary>
    public string Status { get; init; } = string.Empty;
}

/// <summary>备份证据响应。</summary>
public sealed record DatabaseBackupEvidenceV1
{
    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; init; } = string.Empty;

    /// <summary>备份证据业务标识。</summary>
    public string EvidenceNId { get; init; } = string.Empty;

    /// <summary>关联计划业务标识。</summary>
    public string PlanNId { get; init; } = string.Empty;

    /// <summary>关联计划校验和快照。</summary>
    public string PlanChecksum { get; init; } = string.Empty;

    /// <summary>关联计划目标状态指纹快照。</summary>
    public string TargetStateFingerprint { get; init; } = string.Empty;

    /// <summary>备份提供程序标识。</summary>
    public string BackupProvider { get; init; } = string.Empty;

    /// <summary>备份引用(非访问凭据)。</summary>
    public string BackupReference { get; init; } = string.Empty;

    /// <summary>备份捕获时间。</summary>
    public DateTimeOffset CapturedOn { get; init; }

    /// <summary>验证时间(验证后非空)。</summary>
    public DateTimeOffset? VerifiedOn { get; init; }

    /// <summary>备份保留期截止。</summary>
    public DateTimeOffset RetentionUntil { get; init; }

    /// <summary>验证人业务标识。</summary>
    public string? VerifiedByUserNId { get; init; }

    /// <summary>备份证据状态枚举名。</summary>
    public string Status { get; init; } = string.Empty;
}

/// <summary>Operation 响应。</summary>
public sealed record DatabaseOperationV1
{
    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; init; } = string.Empty;

    /// <summary>操作业务标识。</summary>
    public string OperationNId { get; init; } = string.Empty;

    /// <summary>操作类型枚举名。</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>环境业务标识。</summary>
    public string EnvironmentNId { get; init; } = string.Empty;

    /// <summary>服务稳定键。</summary>
    public string ServiceKey { get; init; } = string.Empty;

    /// <summary>模块标识(v2;migration-only v1 为 ServiceKey 兼容值)。</summary>
    public string ModuleKey { get; init; } = string.Empty;

    /// <summary>关联计划业务标识(Apply 操作非空)。</summary>
    public string? PlanNId { get; init; }

    /// <summary>请求的目标迁移版本。</summary>
    public string RequestedVersion { get; init; } = string.Empty;

    /// <summary>幂等键。</summary>
    public string IdempotencyKey { get; init; } = string.Empty;

    /// <summary>操作状态枚举名。</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>当前阶段枚举名。</summary>
    public string Phase { get; init; } = string.Empty;

    /// <summary>当前尝试序号。</summary>
    public int Attempt { get; init; }

    /// <summary>当前租约所有者。</summary>
    public string? LeaseOwner { get; init; }

    /// <summary>入队时间。</summary>
    public DateTimeOffset QueuedOn { get; init; }

    /// <summary>开始执行时间。</summary>
    public DateTimeOffset? StartedOn { get; init; }

    /// <summary>结束时间。</summary>
    public DateTimeOffset? CompletedOn { get; init; }

    /// <summary>超时截止。</summary>
    public DateTimeOffset TimeoutOn { get; init; }

    /// <summary>脱敏错误码。</summary>
    public string? SanitizedErrorCode { get; init; }

    /// <summary>脱敏错误摘要。</summary>
    public string? SanitizedErrorSummary { get; init; }

    /// <summary>追踪标识。</summary>
    public string TraceId { get; init; } = string.Empty;

    /// <summary>发起人业务标识。</summary>
    public string CreatedByUserNId { get; init; } = string.Empty;

    /// <summary>按顺序排列的操作步骤。</summary>
    public IReadOnlyCollection<DatabaseOperationStepV1> Steps { get; init; } = [];

    /// <summary>脱敏种子观察(v2;migration-only v1 为空)。</summary>
    public IReadOnlyCollection<SeedObservationV1>? SeedObservations { get; init; }
}

/// <summary>Operation 步骤响应。</summary>
public sealed record DatabaseOperationStepV1
{
    /// <summary>顺序(1 起)。</summary>
    public int Sequence { get; init; }

    /// <summary>阶段枚举名。</summary>
    public string Phase { get; init; } = string.Empty;

    /// <summary>步骤状态枚举名。</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>本次尝试序号。</summary>
    public int Attempt { get; init; }

    /// <summary>开始时间。</summary>
    public DateTimeOffset? StartedOn { get; init; }

    /// <summary>完成时间。</summary>
    public DateTimeOffset? CompletedOn { get; init; }

    /// <summary>脱敏错误码。</summary>
    public string? SanitizedErrorCode { get; init; }

    /// <summary>脱敏错误摘要。</summary>
    public string? SanitizedErrorSummary { get; init; }
}

/// <summary>入队结果响应(POST /plans、POST /operations/apply)。</summary>
public sealed record EnqueueOperationV1
{
    /// <summary>操作业务标识。</summary>
    public string OperationNId { get; init; } = string.Empty;

    /// <summary>操作类型枚举名。</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>操作状态枚举名。</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>当前阶段枚举名。</summary>
    public string Phase { get; init; } = string.Empty;

    /// <summary>入队时间。</summary>
    public DateTimeOffset AcceptedOn { get; init; }
}

/// <summary>readiness 响应(GET /readiness/{serviceKey};200 Ready 或 503 SD_DB_NOT_READY 携带本形状)。</summary>
public sealed record DatabaseReadinessV1
{
    /// <summary>服务稳定键。</summary>
    public string ServiceKey { get; init; } = string.Empty;

    /// <summary>稳定逻辑库名。</summary>
    public string LogicalDatabaseName { get; init; } = string.Empty;

    /// <summary>脱敏后的物理库目标(隐藏库名中段,不暴露完整目标)。</summary>
    public string PhysicalDatabaseTarget { get; init; } = string.Empty;

    /// <summary>数据库身份指纹。</summary>
    public string DatabaseIdentityFingerprint { get; init; } = string.Empty;

    /// <summary>期望迁移版本。</summary>
    public string DesiredVersion { get; init; } = string.Empty;

    /// <summary>最近观察到的迁移版本(无观察为空)。</summary>
    public string? ObservedVersion { get; init; }

    /// <summary>最近观察时间。</summary>
    public DateTimeOffset? ObservedOn { get; init; }

    /// <summary>拓扑 revision。</summary>
    public string TopologyRevision { get; init; } = string.Empty;

    /// <summary>注册清单产物校验和。</summary>
    public string ArtifactChecksum { get; init; } = string.Empty;

    /// <summary>是否就绪。</summary>
    public bool Ready { get; init; }

    /// <summary>状态标识(<c>Ready</c>/<c>NotReady</c>)。</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>未就绪原因(脱敏,不泄漏连接信息)。</summary>
    public string? Reason { get; init; }

    /// <summary>最近相关操作业务标识。</summary>
    public string? OperationNId { get; init; }

    /// <summary>追踪标识。</summary>
    public string? TraceId { get; init; }
}
