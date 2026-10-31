namespace IndustrialPlatform.PlatformStudio.Domain;

public sealed class StudioWorkbook : StudioEntity
{
    public StudioWorkbook() { }
    public StudioWorkbook(DateTimeOffset createdOn) : base(createdOn) { }
    public string Name { get; set; } = "";
    public string? SourceArtifactNId { get; set; }
    public Guid? CurrentVersionId { get; set; }
    public long VersionSequence { get; set; }
    /// <summary>显式保存的历史版本序号；日常保存工作副本不递增。</summary>
    public long HistorySequence { get; set; }
    public bool HasWorkingCopy { get; set; }
    public string OwnerUserNId { get; set; } = "";
    public bool IsArchived { get; set; }
}
public sealed class StudioWorkbookVersion : StudioTechnicalRecord
{
    /// <summary>为空表示可更新的当前工作副本；非空后文件和内容字段不可变。</summary>
    public long? HistoryVersion { get; set; }
    public DateTimeOffset? PinnedOn { get; set; }
    public string? VersionNote { get; set; }
    public Guid WorkbookId { get; set; }
    public long ContentVersion { get; set; }
    public Guid? BaseVersionId { get; set; }
    public string FileNId { get; set; } = "";
    public string FileReferenceNId { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long ContentLength { get; set; }
    public string SavedBy { get; set; } = "";
    public string SaveOperationKey { get; set; } = "";
}
public sealed class StudioWorkbookSession : StudioTechnicalRecord
{
    public Guid WorkbookId { get; set; }
    public Guid BaseVersionId { get; set; }
    /// <summary>最近确认的内容修订；工作副本主键不变时仍能拒绝旧写入。</summary>
    public long BaseContentVersion { get; set; }
    public string UserNId { get; set; } = "";
    public string ProviderKey { get; set; } = "";
    public string Mode { get; set; } = "View";
    public string Status { get; set; } = "Active";
    public string AccessTokenHash { get; set; } = "";
    public DateTimeOffset ExpiresOn { get; set; }
    public DateTimeOffset LastSeenOn { get; set; }
    public string? LockHash { get; set; }
    public string? LockValueCiphertext { get; set; }
    public DateTimeOffset? LockExpiresOn { get; set; }
    public string? LastSaveOperationKey { get; set; }
    public string IdentityEvidenceJson { get; set; } = "";
}

/// <summary>仅登记被新工作副本替代的临时文件；成功保存同事务入队，交由Files释放及删除流程回收。历史和冲突恢复文件不入队。</summary>
public sealed class StudioWorkbookRetiredFile : StudioTechnicalRecord
{
    public Guid WorkbookId { get; set; }
    public string OwnerUserNId { get; set; } = "";
    public string FileNId { get; set; } = "";
    public string FileReferenceNId { get; set; } = "";
}
