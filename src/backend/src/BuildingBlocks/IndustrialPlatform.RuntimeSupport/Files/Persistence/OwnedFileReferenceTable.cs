using SqlSugar;

namespace IndustrialPlatform.RuntimeSupport.Files;

/// <summary>业务所有权引用，Pending与Active均保护文件不被提前删除；保全限制释放。</summary>
[SugarTable("file_owned_reference")]
public sealed class OwnedFileReferenceTable
{
    /// <summary>已验证租户；与所有者及文件租户一致。</summary>
    [SugarColumn(ColumnName = "tenant_n_id", IsPrimaryKey = true)] public string TenantNId { get; set; } = string.Empty;
    /// <summary>服务器生成的不透明业务引用标识。</summary>
    [SugarColumn(ColumnName = "reference_n_id", IsPrimaryKey = true)] public string ReferenceNId { get; set; } = string.Empty;
    /// <summary>提交后固定的File标识，准备阶段可空。</summary>
    [SugarColumn(ColumnName = "file_n_id")] public string? FileNId { get; set; }
    /// <summary>受信所属服务，首期仅platform-studio。</summary>
    [SugarColumn(ColumnName = "owner_service")] public string OwnerService { get; set; } = string.Empty;
    /// <summary>明确的资源所有权种类。</summary>
    [SugarColumn(ColumnName = "owner_kind")] public string OwnerKind { get; set; } = string.Empty;
    /// <summary>所属资源或执行对象的稳定业务键。</summary>
    [SugarColumn(ColumnName = "owner_n_id")] public string OwnerNId { get; set; } = string.Empty;
    /// <summary>同租户和所属服务唯一的准备幂等键。</summary>
    [SugarColumn(ColumnName = "operation_key")] public string OperationKey { get; set; } = string.Empty;
    /// <summary>Pending/Active/Released，不因扫描未知而变Active。</summary>
    [SugarColumn(ColumnName = "status")] public string Status { get; set; } = string.Empty;
    /// <summary>引用CAS版本，从0开始。</summary>
    [SugarColumn(ColumnName = "version")] public long Version { get; set; }
    /// <summary>首次准备的UTC时间。</summary>
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
    /// <summary>Pending回收的最早时点，不表示可自动释放Unknown。</summary>
    [SugarColumn(ColumnName = "expires_on")] public DateTimeOffset? ExpiresOn { get; set; }
    /// <summary>确认解除引用的UTC时间。</summary>
    [SugarColumn(ColumnName = "released_on")] public DateTimeOffset? ReleasedOn { get; set; }
    /// <summary>在此UTC时间前不能释放。</summary>
    [SugarColumn(ColumnName = "retention_until")] public DateTimeOffset? RetentionUntil { get; set; }
    /// <summary>有法律保全时禁止释放。</summary>
    [SugarColumn(ColumnName = "legal_hold")] public bool LegalHold { get; set; }
    /// <summary>法律保全原因，启用时必填。</summary>
    [SugarColumn(ColumnName = "hold_reason")] public string? HoldReason { get; set; }
    /// <summary>最后一次受控状态推进时点。</summary>
    [SugarColumn(ColumnName = "last_updated_on")] public DateTimeOffset LastUpdatedOn { get; set; }
}
