namespace IndustrialPlatform.SystemData.Contracts.DatabaseOrchestration;

// =====================================================================
// 服务初始化公开契约 v2(TASK-SD-004,/api/v1/service-initialization/**)。
// 约定同 v1:
//  - 请求属性一律 string?/可空枚举(防 [ApiController] Required 推断破坏统一信封);
//  - 枚举以枚举名字符串传输,不依赖 JSON 数字;
//  - 不暴露数据库地址、角色密码、SecretRef、SQL 或原始种子/迁移输出;
//  - 环境(NId)由服务端可信拓扑解析,请求不含环境标识;
//  - 契约扫描禁止属性名含 Secret/Password/Token/ConnectionString,
//    因此 bootstrap 状态属性命名为 BootstrapReady/BootstrapStatus。
// =====================================================================

/// <summary>
/// 服务初始化注册清单请求(PUT /api/v1/service-initialization/registrations/{serviceKey}/{moduleKey})。
/// v1 <see cref="DatabaseRegistrationManifestV1"/> 为无 SeedSets 的兼容输入(v1 端点映射 moduleKey=serviceKey);
/// 一旦声明 SeedSets,初始化必须补齐 RequiredSeed/SecretBootstrap 阶段后才 Healthy(蓝图 §2)。
/// </summary>
public sealed record ServiceInitializationManifestV2
{
    /// <summary>服务稳定键。</summary>
    public string? ServiceKey { get; init; }

    /// <summary>强制模块标识;独立服务也必须声明,禁止宿主级模糊大包。</summary>
    public string? ModuleKey { get; init; }

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

    /// <summary>本模块版本化种子声明集合;不含种子实际内容。</summary>
    public IReadOnlyCollection<SeedSetV1>? SeedSets { get; init; }
}

/// <summary>
/// 种子声明集合元素(蓝图 §3)。仅声明非敏感需求,不含种子内容、Secret、SQL 或命令。
/// <c>AllowedEnvironments</c> 为逗号分隔环境种类(Development/Test/Staging/Production,空表默认全环境);
/// <c>DependsOnSeedKeys</c> 为逗号分隔前置 SeedKey(同模块内)。
/// </summary>
public sealed record SeedSetV1
{
    /// <summary>稳定种子键。</summary>
    public string? SeedKey { get; init; }

    /// <summary>不可变种子版本。</summary>
    public string? SeedVersion { get; init; }

    /// <summary>种子类别枚举名(SystemBaseline/TenantBaseline/EnvironmentSample/SecretBootstrap)。</summary>
    public string? SeedClass { get; init; }

    /// <summary>种子作用域。</summary>
    public string? Scope { get; init; }

    /// <summary>种子产物标识(签名 SQL seed bundle 或 initializer bundle)。</summary>
    public string? SeedArtifactId { get; init; }

    /// <summary>种子产物校验和(SHA-256 十六进制)。</summary>
    public string? SeedChecksum { get; init; }

    /// <summary>种子产物签名引用(仅非敏感引用)。</summary>
    public string? SeedSignature { get; init; }

    /// <summary>是否影响 readiness(默认 SystemBaseline 为 true)。</summary>
    public bool? RequiredForReadiness { get; init; }

    /// <summary>允许执行的环境(逗号分隔;Staging/Production 拒绝 EnvironmentSample)。</summary>
    public string? AllowedEnvironments { get; init; }

    /// <summary>前置迁移版本,未达到则拒绝执行。</summary>
    public string? DependsOnMigrationVersion { get; init; }

    /// <summary>前置 SeedKey(逗号分隔,同模块)。</summary>
    public string? DependsOnSeedKeys { get; init; }

    /// <summary>bootstrap 交付策略枚举名(仅 SecretBootstrap 有意义)。</summary>
    public string? BootstrapPolicy { get; init; }
}

/// <summary>
/// 初始化计划请求(POST /api/v1/service-initialization/plans,入队异步计划)。
/// 按 (ServiceKey, ModuleKey) 粒度;返回 202 Operation。
/// </summary>
public sealed record ServiceInitializationPlanRequestV2
{
    /// <summary>服务稳定键。</summary>
    public string? ServiceKey { get; init; }

    /// <summary>模块标识。</summary>
    public string? ModuleKey { get; init; }

    /// <summary>请求的目标迁移版本。</summary>
    public string? RequestedVersion { get; init; }

    /// <summary>期望数据状态枚举名(缺省沿用注册清单)。</summary>
    public string? DesiredState { get; init; }
}

/// <summary>
/// 初始化 apply 请求(POST /api/v1/service-initialization/operations/apply,入队异步 apply)。
/// 幂等哈希输入须与计划一致;返回 202 Operation。
/// </summary>
public sealed record ServiceInitializationApplyRequestV2
{
    /// <summary>已生成计划业务标识。</summary>
    public string? PlanNId { get; init; }

    /// <summary>模块标识(幂等哈希输入,须与计划一致)。</summary>
    public string? ModuleKey { get; init; }

    /// <summary>请求的目标迁移版本(幂等哈希输入,须与计划一致)。</summary>
    public string? RequestedVersion { get; init; }
}

/// <summary>
/// 脱敏种子观察(控制面 <c>system_data_seed_observation</c>;蓝图 §5.3)。
/// 本地账本是权威,SystemData 只保存脱敏观察;不含种子内容或 Secret。
/// </summary>
public sealed record SeedObservationV1
{
    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; init; } = string.Empty;

    /// <summary>环境业务标识。</summary>
    public string EnvironmentNId { get; init; } = string.Empty;

    /// <summary>服务稳定键。</summary>
    public string ServiceKey { get; init; } = string.Empty;

    /// <summary>模块标识。</summary>
    public string ModuleKey { get; init; } = string.Empty;

    /// <summary>稳定种子键。</summary>
    public string SeedKey { get; init; } = string.Empty;

    /// <summary>种子版本。</summary>
    public string SeedVersion { get; init; } = string.Empty;

    /// <summary>种子产物校验和。</summary>
    public string Checksum { get; init; } = string.Empty;

    /// <summary>种子作用域。</summary>
    public string Scope { get; init; } = string.Empty;

    /// <summary>种子状态枚举名(Applied/Pending/Failed/Skipped)。</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>应用时间。</summary>
    public DateTimeOffset? AppliedOn { get; init; }

    /// <summary>关联操作业务标识。</summary>
    public string? OperationNId { get; init; }

    /// <summary>校验状态枚举名。</summary>
    public string VerificationStatus { get; init; } = string.Empty;
}

/// <summary>就绪响应中的单个种子状态(模块级)。</summary>
public sealed record SeedReadinessV2
{
    /// <summary>稳定种子键。</summary>
    public string SeedKey { get; init; } = string.Empty;

    /// <summary>种子版本。</summary>
    public string SeedVersion { get; init; } = string.Empty;

    /// <summary>种子状态枚举名。</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>应用时间。</summary>
    public DateTimeOffset? AppliedOn { get; init; }
}

/// <summary>
/// 服务初始化 readiness(GET /api/v1/service-initialization/readiness/{serviceKey}/{moduleKey})。
/// 200 时 <c>Ready=true</c>;未达 exact desired state 时 503 携带本形状 <c>Ready=false</c>(NotReady,复用
/// <see cref="DatabaseReadinessV1"/> 语义)。不含连接串、SQLite 路径或任何凭据。
/// </summary>
public sealed record ServiceInitializationReadinessV2
{
    /// <summary>服务稳定键。</summary>
    public string ServiceKey { get; init; } = string.Empty;

    /// <summary>模块标识。</summary>
    public string ModuleKey { get; init; } = string.Empty;

    /// <summary>稳定逻辑库名。</summary>
    public string LogicalDatabaseName { get; init; } = string.Empty;

    /// <summary>脱敏后的物理库目标(隐藏库名中段,不暴露完整目标)。</summary>
    public string PhysicalDatabaseTarget { get; init; } = string.Empty;

    /// <summary>数据库身份指纹。</summary>
    public string DatabaseIdentityFingerprint { get; init; } = string.Empty;

    /// <summary>迁移产物校验和。</summary>
    public string ArtifactChecksum { get; init; } = string.Empty;

    /// <summary>期望迁移版本。</summary>
    public string DesiredMigrationVersion { get; init; } = string.Empty;

    /// <summary>最近观察到的迁移版本(无观察为空)。</summary>
    public string? ObservedMigrationVersion { get; init; }

    /// <summary>最近观察时间。</summary>
    public DateTimeOffset? ObservedOn { get; init; }

    /// <summary>拓扑 revision。</summary>
    public string TopologyRevision { get; init; } = string.Empty;

    /// <summary>迁移是否达到期望版本。</summary>
    public bool MigrationReady { get; init; }

    /// <summary>RequiredForReadiness 种子是否全部到达期望版本。</summary>
    public bool RequiredSeedReady { get; init; }

    /// <summary>SecretBootstrap(按需)是否完成。</summary>
    public bool BootstrapReady { get; init; }

    /// <summary>各种子就绪状态(模块级明细)。</summary>
    public IReadOnlyCollection<SeedReadinessV2>? Seeds { get; init; }

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
