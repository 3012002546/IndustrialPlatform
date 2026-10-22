using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.PlatformStudio.Domain;

/// <summary>Studio可编辑实体共同生命周期。物理主键与对外NId分开；版本从0开始。</summary>
public abstract class StudioEntity : Entity
{
    protected StudioEntity() { }
    protected StudioEntity(DateTimeOffset createdOn) : base(createdOn) { }

    public string NId { get; set; } = "";
    public string TenantNId { get; set; } = "";

    public void EnsureWritable() => EnsureCanModify();
    public void MarkChanged(DateTimeOffset now) { EnsureCanModify(); Touch(now); }
}

/// <summary>发布资源主档：草稿、发布指向和分配序号归同一资源，不用通用任意对象表。</summary>
public sealed class StudioResource : StudioEntity
{
    public StudioResource() { }
    public StudioResource(DateTimeOffset createdOn) : base(createdOn) { }
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Folder { get; set; }
    public int DocumentSchemaVersion { get; set; } = 1;
    public string DraftJson { get; set; } = "{}";
    public Guid? DraftBaseRevisionId { get; set; }
    public Guid? PublishedRevisionId { get; set; }
    public long RevisionSequence { get; set; }
    public bool IsEnabled { get; set; }
    public string OwnerUserNId { get; set; } = "";
}

/// <summary>宿主技术记录共同键；没有可编辑实体的冻结/删除标记。</summary>
public abstract class StudioTechnicalRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string NId { get; set; } = "";
    public string TenantNId { get; set; } = "";
    public DateTimeOffset CreatedOn { get; set; }
}

/// <summary>不可变发布修订：文档、来源合同和依赖闭包在同一次发布中固定。</summary>
public sealed class StudioRevision : StudioTechnicalRecord
{
    public Guid ResourceId { get; set; }
    public long RevisionNo { get; set; }
    public int DocumentSchemaVersion { get; set; } = 1;
    public string DocumentJson { get; set; } = "{}";
    public string DependenciesJson { get; set; } = "[]";
    public string SourcePinsJson { get; set; } = "[]";
    public string ContentHash { get; set; } = "";
    public string PublishedBy { get; set; } = "";
    public string ReleaseNote { get; set; } = "";
}

/// <summary>资源的主体/角色能力授予。与可信功能权限取交集，撤销不删除审计依据。</summary>
public sealed class StudioGrant : StudioTechnicalRecord
{
    public Guid ResourceId { get; set; }
    public string SubjectKind { get; set; } = "User";
    public string SubjectNId { get; set; } = "";
    public string Capability { get; set; } = "View";
    public DateTimeOffset? RevokedOn { get; set; }
    public string GrantedBy { get; set; } = "";
}

/// <summary>受控数据源；仅存连接/凭据登记键，秘密不进入持久配置文档或DTO。</summary>
public sealed class StudioSource : StudioEntity
{
    public StudioSource() { }
    public StudioSource(DateTimeOffset createdOn) : base(createdOn) { }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Provider { get; set; } = "RegisteredApi";
    public string EndpointKey { get; set; } = "";
    /// <summary>登记目标的非秘密语义摘要，防止部署配置暗换同一endpointKey的实际目标。</summary>
    public string EndpointConfigurationHash { get; set; } = "";
    public string? CredentialRef { get; set; }
    public string? CatalogKey { get; set; }
    public string AllowedObjectsJson { get; set; } = "[]";
    public bool IsEnabled { get; set; }
    public long ConfigurationVersion { get; set; } = 1;
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRows { get; set; } = 10000;
    public long MaxBytes { get; set; } = 10485760;
}

/// <summary>Studio自有台账的一条记录；ValuesJson按稳定fieldId保存真值，不借用ReferenceData。</summary>
public sealed class StudioLedgerRecord : StudioEntity
{
    public StudioLedgerRecord() { }
    public StudioLedgerRecord(DateTimeOffset createdOn) : base(createdOn) { }
    public Guid ModelResourceId { get; set; }
    public Guid SchemaRevisionId { get; set; }
    public string ValuesJson { get; set; } = "{}";
    public string CreatedBy { get; set; } = "";
    public string LastUpdatedBy { get; set; } = "";
}

/// <summary>台账单字段类型索引；完整值仍在记录JSON中，二者必须同事务更新。</summary>
public sealed class StudioLedgerValue
{
    public Guid RecordId { get; set; }
    public string TenantNId { get; set; } = "";
    public Guid ModelResourceId { get; set; }
    public string FieldId { get; set; } = "";
    public string ValueKind { get; set; } = "";
    public string? TextValue { get; set; }
    public long? NumberScaled { get; set; }
    public bool? BoolValue { get; set; }
    public DateTimeOffset? TimeValue { get; set; }
    public string? UniqueKey { get; set; }
}

/// <summary>有副作用操作的持久防重回执。Pending/Unknown不能自动重发或按超时删除。</summary>
public sealed class StudioOperationReceipt : StudioTechnicalRecord
{
    public string OperationKey { get; set; } = "";
    public string ActorUserNId { get; set; } = "";
    public string OperationKind { get; set; } = "";
    public string ResourceNId { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string? ResponseJson { get; set; }
    /// <summary>页面动作固定的代码执行器键；非页面动作为空。</summary>
    public string? ExecutorKey { get; set; }
    /// <summary>受理时固定的执行器合同版本。</summary>
    public string? ExecutorVersion { get; set; }
    public string? ExternalReference { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset? CompletedOn { get; set; }
}

/// <summary>与业务写入同事务追加的可靠审计投递记录；不替代审计事实查询。</summary>
public sealed class StudioAuditOutbox : StudioTechnicalRecord
{
    public string ModuleKey { get; set; } = "studio-core";
    public string AuditEventNId { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptOn { get; set; }
    public DateTimeOffset? SentOn { get; set; }
    public string? LastErrorCode { get; set; }
}

/// <summary>一次外部登录相关性请求；原始state/nonce不入日志，verifier通过DataProtection加密。</summary>
public sealed class StudioAuthRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string StateHash { get; set; } = "";
    public string IssuerKey { get; set; } = "";
    public string Provider { get; set; } = "";
    public string NonceHash { get; set; } = "";
    public string VerifierCipher { get; set; } = "";
    public string ReturnPath { get; set; } = "/pc/studio/low-code";
    public string Status { get; set; } = "Created";
    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset ExpiresOn { get; set; }
    public DateTimeOffset? ConsumedOn { get; set; }
    public long OptimisticVersion { get; set; }
}

/// <summary>一个已验证外部/本地会话；浏览器只保存随机cookie，服务器保存hash和加密上下文依据。</summary>
public sealed class StudioAuthSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SessionHash { get; set; } = "";
    public string TenantNId { get; set; } = "";
    public string SubjectNId { get; set; } = "";
    public string IssuerKey { get; set; } = "";
    public string Provider { get; set; } = "";
    public string? ExternalSubjectId { get; set; }
    public string? ExternalTenantId { get; set; }
    public string? ContextHandleCipher { get; set; }
    public string PrincipalJson { get; set; } = "{}";
    public string AuthVersion { get; set; } = "";
    public long PolicyVersion { get; set; }
    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset LastSeenOn { get; set; }
    public DateTimeOffset ContextCheckedOn { get; set; }
    public DateTimeOffset ExpiresOn { get; set; }
    public DateTimeOffset? RevokedOn { get; set; }
    public long OptimisticVersion { get; set; }
}

/// <summary>无MES装配下每租户唯一的最小管理员；不复制用户中心或MES密码。</summary>
public sealed class StudioLocalAdmin
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantNId { get; set; } = "";
    public string SubjectNId { get; set; } = "";
    public string LoginName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool MustChangePassword { get; set; } = true;
    public long AuthVersion { get; set; } = 1;
    public int FailedCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset LastUpdatedOn { get; set; }
    public long OptimisticVersion { get; set; }
}
