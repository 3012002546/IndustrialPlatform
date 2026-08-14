using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration.Events;

namespace IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

/// <summary>
/// 服务数据库注册清单聚合根(05 方案 §8.1 <c>system_data_database_registration</c>)。
/// 保留 ServiceKey/LogicalDatabaseName 逻辑身份与解析出的 PhysicalDatabaseName/拓扑 revision;
/// 按 (TenantNId, EnvironmentNId, ServiceKey) 唯一。注册发布 <see cref="DatabaseRegistrationChangedEvent"/>。
/// </summary>
public sealed class DatabaseRegistration : AggregateRoot
{
    /// <summary>ServiceKey 最大长度。</summary>
    public const int ServiceKeyMaxLength = 128;

    /// <summary>Provider 标识最大长度。</summary>
    public const int ProviderMaxLength = 64;

    /// <summary>逻辑/物理库名最大长度。</summary>
    public const int DatabaseNameMaxLength = 128;

    /// <summary>拓扑模式标识最大长度。</summary>
    public const int TopologyModeMaxLength = 32;

    /// <summary>迁移产物标识最大长度。</summary>
    public const int MigrationArtifactIdMaxLength = 128;

    /// <summary>版本串最大长度。</summary>
    public const int VersionMaxLength = 64;

    /// <summary>签名最大长度。</summary>
    public const int SignatureMaxLength = 512;

    /// <summary>模块标识最大长度。</summary>
    public const int ModuleKeyMaxLength = 128;

    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; private set; }

    /// <summary>环境业务标识。</summary>
    public string EnvironmentNId { get; private set; }

    /// <summary>服务稳定键。</summary>
    public string ServiceKey { get; private set; }

    /// <summary>强制模块标识;注册身份按 (TenantNId, EnvironmentNId, ServiceKey, ModuleKey) 唯一,v1 兼容默认 = ServiceKey。</summary>
    public string ModuleKey { get; private set; }

    /// <summary>数据库提供程序标识(<c>Sqlite</c>/<c>PostgreSQL</c>)。</summary>
    public string Provider { get; private set; }

    /// <summary>稳定逻辑库名。</summary>
    public string LogicalDatabaseName { get; private set; }

    /// <summary>解析出的物理库名。</summary>
    public string PhysicalDatabaseName { get; private set; }

    /// <summary>是否与其他服务共享物理数据库。</summary>
    public bool IsSharedPhysicalDatabase { get; private set; }

    /// <summary>拓扑模式(<c>Shared</c>/<c>PerService</c>)。</summary>
    public string TopologyMode { get; private set; }

    /// <summary>受信任拓扑 revision(指纹)。</summary>
    public string TopologyRevision { get; private set; }

    /// <summary>迁移产物标识。</summary>
    public string MigrationArtifactId { get; private set; }

    /// <summary>迁移版本。</summary>
    public string MigrationVersion { get; private set; }

    /// <summary>迁移产物校验和。</summary>
    public string ArtifactChecksum { get; private set; }

    /// <summary>产物签名引用(仅非敏感引用,绝不保存私钥/凭据)。</summary>
    public string? ArtifactSignature { get; private set; }

    /// <summary>负责该数据库的运营/所有者业务标识。</summary>
    public string OwnerNId { get; private set; }

    /// <summary>期望数据状态。</summary>
    public DesiredState DesiredState { get; private set; }

    /// <summary>是否允许自动 provision(创建数据库/角色)。</summary>
    public bool AutoProvision { get; private set; }

    /// <summary>是否允许自动迁移。</summary>
    public bool AutoMigrate { get; private set; }

    /// <summary>公开清单版本。</summary>
    public string ManifestVersion { get; private set; }

    /// <summary>清单校验和(重注册幂等依据)。</summary>
    public string ManifestChecksum { get; private set; }

    /// <summary>注册清单状态。</summary>
    public RegistrationStatus Status { get; private set; }

    /// <summary>本模块版本化种子声明集合(不含种子内容,只含校验和等非敏感元数据)。</summary>
    public IReadOnlyCollection<SeedSet> SeedSets { get; private set; }

    private DatabaseRegistration()
    {
        TenantNId = string.Empty;
        EnvironmentNId = string.Empty;
        ServiceKey = string.Empty;
        ModuleKey = string.Empty;
        Provider = string.Empty;
        LogicalDatabaseName = string.Empty;
        PhysicalDatabaseName = string.Empty;
        TopologyMode = string.Empty;
        TopologyRevision = string.Empty;
        MigrationArtifactId = string.Empty;
        MigrationVersion = string.Empty;
        ArtifactChecksum = string.Empty;
        OwnerNId = string.Empty;
        ManifestVersion = string.Empty;
        ManifestChecksum = string.Empty;
        SeedSets = [];
    }

    private DatabaseRegistration(
        string tenantNId,
        string environmentNId,
        string serviceKey,
        string moduleKey,
        string provider,
        string logicalDatabaseName,
        string physicalDatabaseName,
        bool isSharedPhysicalDatabase,
        string topologyMode,
        string topologyRevision,
        string migrationArtifactId,
        string migrationVersion,
        string artifactChecksum,
        string? artifactSignature,
        string ownerNId,
        DesiredState desiredState,
        bool autoProvision,
        bool autoMigrate,
        string manifestVersion,
        string manifestChecksum,
        IReadOnlyCollection<SeedSet>? seedSets)
    {
        TenantNId = DatabaseOrchestrationGuard.RequireNId(tenantNId, "注册清单的租户标识不能为空。");
        EnvironmentNId = DatabaseOrchestrationGuard.RequireNId(environmentNId, "注册清单的环境标识不能为空。");
        ServiceKey = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            serviceKey, "服务键不能为空。", ServiceKeyMaxLength, $"服务键长度不能超过 {ServiceKeyMaxLength} 个字符。");
        ModuleKey = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            moduleKey, "模块标识不能为空。", ModuleKeyMaxLength, $"模块标识长度不能超过 {ModuleKeyMaxLength} 个字符。");
        Provider = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            provider, "数据库提供程序不能为空。", ProviderMaxLength, $"提供程序标识长度不能超过 {ProviderMaxLength} 个字符。");
        LogicalDatabaseName = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            logicalDatabaseName, "逻辑库名不能为空。", DatabaseNameMaxLength, $"逻辑库名长度不能超过 {DatabaseNameMaxLength} 个字符。");
        PhysicalDatabaseName = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            physicalDatabaseName, "物理库名不能为空。", DatabaseNameMaxLength, $"物理库名长度不能超过 {DatabaseNameMaxLength} 个字符。");
        IsSharedPhysicalDatabase = isSharedPhysicalDatabase;
        TopologyMode = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            topologyMode, "拓扑模式不能为空。", TopologyModeMaxLength, $"拓扑模式标识长度不能超过 {TopologyModeMaxLength} 个字符。");
        TopologyRevision = DatabaseOrchestrationGuard.RequireSha256Hex(topologyRevision, "拓扑 revision 不能为空。");
        MigrationArtifactId = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            migrationArtifactId, "迁移产物标识不能为空。", MigrationArtifactIdMaxLength, $"迁移产物标识长度不能超过 {MigrationArtifactIdMaxLength} 个字符。");
        MigrationVersion = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            migrationVersion, "迁移版本不能为空。", VersionMaxLength, $"迁移版本长度不能超过 {VersionMaxLength} 个字符。");
        ArtifactChecksum = DatabaseOrchestrationGuard.RequireSha256Hex(artifactChecksum, "产物校验和不能为空。");
        ArtifactSignature = DatabaseOrchestrationGuard.TrimOrNull(artifactSignature, SignatureMaxLength, $"签名引用长度不能超过 {SignatureMaxLength} 个字符。");
        OwnerNId = DatabaseOrchestrationGuard.RequireNId(ownerNId, "注册清单的所有者标识不能为空。");
        DesiredState = desiredState;
        AutoProvision = autoProvision;
        AutoMigrate = autoMigrate;
        ManifestVersion = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            manifestVersion, "清单版本不能为空。", VersionMaxLength, $"清单版本长度不能超过 {VersionMaxLength} 个字符。");
        ManifestChecksum = DatabaseOrchestrationGuard.RequireSha256Hex(manifestChecksum, "清单校验和不能为空。");
        SeedSets = SeedSetGuard.Validate(seedSets);
        Status = RegistrationStatus.Registered;
        AddDomainEvent(CreateChangedEvent());
    }

    /// <summary>持久化层重建专用构造,不重新校验、不发布事件。</summary>
    internal DatabaseRegistration(
        Guid id,
        string tenantNId,
        string environmentNId,
        string serviceKey,
        string moduleKey,
        string provider,
        string logicalDatabaseName,
        string physicalDatabaseName,
        bool isSharedPhysicalDatabase,
        string topologyMode,
        string topologyRevision,
        string migrationArtifactId,
        string migrationVersion,
        string artifactChecksum,
        string? artifactSignature,
        string ownerNId,
        DesiredState desiredState,
        bool autoProvision,
        bool autoMigrate,
        string manifestVersion,
        string manifestChecksum,
        IReadOnlyCollection<SeedSet>? seedSets,
        RegistrationStatus status,
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
        Provider = provider;
        LogicalDatabaseName = logicalDatabaseName;
        PhysicalDatabaseName = physicalDatabaseName;
        IsSharedPhysicalDatabase = isSharedPhysicalDatabase;
        TopologyMode = topologyMode;
        TopologyRevision = topologyRevision;
        MigrationArtifactId = migrationArtifactId;
        MigrationVersion = migrationVersion;
        ArtifactChecksum = artifactChecksum;
        ArtifactSignature = artifactSignature;
        OwnerNId = ownerNId;
        DesiredState = desiredState;
        AutoProvision = autoProvision;
        AutoMigrate = autoMigrate;
        ManifestVersion = manifestVersion;
        ManifestChecksum = manifestChecksum;
        SeedSets = seedSets ?? [];
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

    /// <summary>创建注册清单(Status = Registered)。v1 兼容:moduleKey = serviceKey、SeedSets 空。</summary>
    public static DatabaseRegistration Register(
        string tenantNId,
        string environmentNId,
        string serviceKey,
        string provider,
        string logicalDatabaseName,
        string physicalDatabaseName,
        bool isSharedPhysicalDatabase,
        string topologyMode,
        string topologyRevision,
        string migrationArtifactId,
        string migrationVersion,
        string artifactChecksum,
        string? artifactSignature,
        string ownerNId,
        DesiredState desiredState,
        bool autoProvision,
        bool autoMigrate,
        string manifestVersion,
        string manifestChecksum,
        IReadOnlyCollection<SeedSet>? seedSets = null,
        string? moduleKey = null)
        => new(
            tenantNId,
            environmentNId,
            serviceKey,
            moduleKey ?? serviceKey,
            provider,
            logicalDatabaseName,
            physicalDatabaseName,
            isSharedPhysicalDatabase,
            topologyMode,
            topologyRevision,
            migrationArtifactId,
            migrationVersion,
            artifactChecksum,
            artifactSignature,
            ownerNId,
            desiredState,
            autoProvision,
            autoMigrate,
            manifestVersion,
            manifestChecksum,
            seedSets);

    /// <summary>以新版本清单重注册(应用层已裁决版本冲突),发布变更事件。模块身份不可变。</summary>
    public void ReRegister(
        string provider,
        string logicalDatabaseName,
        string physicalDatabaseName,
        bool isSharedPhysicalDatabase,
        string topologyMode,
        string topologyRevision,
        string migrationArtifactId,
        string migrationVersion,
        string artifactChecksum,
        string? artifactSignature,
        DesiredState desiredState,
        bool autoProvision,
        bool autoMigrate,
        string manifestVersion,
        string manifestChecksum,
        IReadOnlyCollection<SeedSet>? seedSets = null)
    {
        EnsureCanModify();
        Provider = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            provider, "数据库提供程序不能为空。", ProviderMaxLength, $"提供程序标识长度不能超过 {ProviderMaxLength} 个字符。");
        LogicalDatabaseName = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            logicalDatabaseName, "逻辑库名不能为空。", DatabaseNameMaxLength, $"逻辑库名长度不能超过 {DatabaseNameMaxLength} 个字符。");
        PhysicalDatabaseName = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            physicalDatabaseName, "物理库名不能为空。", DatabaseNameMaxLength, $"物理库名长度不能超过 {DatabaseNameMaxLength} 个字符。");
        IsSharedPhysicalDatabase = isSharedPhysicalDatabase;
        TopologyMode = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            topologyMode, "拓扑模式不能为空。", TopologyModeMaxLength, $"拓扑模式标识长度不能超过 {TopologyModeMaxLength} 个字符。");
        TopologyRevision = DatabaseOrchestrationGuard.RequireSha256Hex(topologyRevision, "拓扑 revision 不能为空。");
        MigrationArtifactId = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            migrationArtifactId, "迁移产物标识不能为空。", MigrationArtifactIdMaxLength, $"迁移产物标识长度不能超过 {MigrationArtifactIdMaxLength} 个字符。");
        MigrationVersion = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            migrationVersion, "迁移版本不能为空。", VersionMaxLength, $"迁移版本长度不能超过 {VersionMaxLength} 个字符。");
        ArtifactChecksum = DatabaseOrchestrationGuard.RequireSha256Hex(artifactChecksum, "产物校验和不能为空。");
        ArtifactSignature = DatabaseOrchestrationGuard.TrimOrNull(artifactSignature, SignatureMaxLength, $"签名引用长度不能超过 {SignatureMaxLength} 个字符。");
        DesiredState = desiredState;
        AutoProvision = autoProvision;
        AutoMigrate = autoMigrate;
        ManifestVersion = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            manifestVersion, "清单版本不能为空。", VersionMaxLength, $"清单版本长度不能超过 {VersionMaxLength} 个字符。");
        ManifestChecksum = DatabaseOrchestrationGuard.RequireSha256Hex(manifestChecksum, "清单校验和不能为空。");
        SeedSets = SeedSetGuard.Validate(seedSets);
        Status = RegistrationStatus.Registered;
        AddDomainEvent(CreateChangedEvent());
        Touch();
    }

    /// <summary>更新期望数据状态,发布变更事件。</summary>
    public void UpdateDesiredState(DesiredState desiredState)
    {
        EnsureCanModify();
        DesiredState = desiredState;
        AddDomainEvent(CreateChangedEvent());
        Touch();
    }

    /// <summary>标记清单为未就绪(SD-003 应用/验证失败后由 Runner 调用)。</summary>
    public void MarkNotReady()
    {
        EnsureCanModify();
        Status = RegistrationStatus.NotReady;
        Touch();
    }

    private DatabaseRegistrationChangedEvent CreateChangedEvent() =>
        new(
            TenantNId,
            EnvironmentNId,
            ServiceKey,
            ModuleKey,
            LogicalDatabaseName,
            DesiredState,
            ManifestVersion,
            ManifestChecksum);
}
