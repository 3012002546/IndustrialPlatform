namespace IndustrialPlatform.SystemData.Contracts.Administration;

// =====================================================================
// 行政组织公开契约(TASK-SD-006,05 方案 §9.3)。
// 约定:
//  - 请求属性一律 string?/可空值类型(防 [ApiController] Required 推断破坏统一信封);
//  - 枚举以枚举名字符串传输(如 "Company"/"Active"),不依赖 JSON 数字;
//  - 响应不暴露数据库 Guid 主键,只暴露稳定 NId 与双并发版本;
//  - 跨租户 NId 统一返回 404,不回显对象存在性。
// =====================================================================

/// <summary>创建根公司或子组织请求(POST /organizations)。</summary>
public sealed record CreateOrganizationRequest
{
    /// <summary>组织业务标识(租户内全历史唯一)。</summary>
    public string? NId { get; init; }

    /// <summary>组织名称(同父下活动组织大小写不敏感唯一)。</summary>
    public string? Name { get; init; }

    /// <summary>组织类型枚举名(Company/Department/Section/Team)。</summary>
    public string? Type { get; init; }

    /// <summary>父组织业务标识(创建根公司时为空)。</summary>
    public string? ParentOrganizationNId { get; init; }

    /// <summary>同父节点内的显示顺序(非负)。</summary>
    public int? DisplayOrder { get; init; }
}

/// <summary>修改组织名称、顺序请求(PUT /organizations/{organizationNId})。</summary>
public sealed record UpdateOrganizationRequest
{
    /// <summary>组织名称。</summary>
    public string? Name { get; init; }

    /// <summary>同父节点内的显示顺序(非负)。</summary>
    public int? DisplayOrder { get; init; }

    /// <summary>乐观版本(双并发版本之一,从 GET 详情读取)。</summary>
    public long? ExpectedOptimisticVersion { get; init; }

    /// <summary>并发版本(双并发版本之一,从 GET 详情读取)。</summary>
    public Guid? ExpectedConcurrencyVersion { get; init; }
}

/// <summary>
/// 移动组织请求(POST /organizations/{organizationNId}/move)。
/// 必须携带移动预览返回的 OrganizationRevision 与双并发版本,过期则 409。
/// </summary>
public sealed record MoveOrganizationRequest
{
    /// <summary>目标父组织业务标识(非公司组织移动时必须指定)。</summary>
    public string? TargetParentOrganizationNId { get; init; }

    /// <summary>预览返回的移动成功后树修订号。</summary>
    public long? PreviewOrganizationRevision { get; init; }

    /// <summary>当前组织乐观版本(从移动预览读取)。</summary>
    public long? ExpectedOptimisticVersion { get; init; }

    /// <summary>当前组织并发版本(从移动预览读取)。</summary>
    public Guid? ExpectedConcurrencyVersion { get; init; }

    /// <summary>移动原因(可选,写入本地审计)。</summary>
    public string? Reason { get; init; }
}

/// <summary>组织移动预览请求(POST /organizations/{organizationNId}/move-preview)。</summary>
public sealed record MoveOrganizationPreviewRequest
{
    /// <summary>目标父组织业务标识(非公司组织必须指定)。</summary>
    public string? TargetParentOrganizationNId { get; init; }
}

/// <summary>启用/停用组织请求(PUT /organizations/{organizationNId}/status)。</summary>
public sealed record SetOrganizationStatusRequest
{
    /// <summary>目标状态枚举名(Active/Inactive)。</summary>
    public string? Status { get; init; }

    /// <summary>原因(可选,写入本地审计)。</summary>
    public string? Reason { get; init; }
}

/// <summary>组织森林树节点(GET /organizations/tree)。</summary>
public sealed record OrganizationNodeV1
{
    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; init; } = string.Empty;

    /// <summary>组织业务标识。</summary>
    public string NId { get; init; } = string.Empty;

    /// <summary>组织名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>组织类型枚举名。</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>组织状态枚举名。</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>父组织业务标识(公司根为空)。</summary>
    public string? ParentOrganizationNId { get; init; }

    /// <summary>同父节点内的显示顺序。</summary>
    public int DisplayOrder { get; init; }

    /// <summary>子树节点集合。</summary>
    public List<OrganizationNodeV1> Children { get; init; } = [];
}

/// <summary>组织详情与并发版本(GET /organizations/{organizationNId})。</summary>
public sealed record OrganizationDetailV1
{
    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; init; } = string.Empty;

    /// <summary>组织业务标识。</summary>
    public string NId { get; init; } = string.Empty;

    /// <summary>组织名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>组织类型枚举名。</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>组织状态枚举名。</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>父组织业务标识(公司根为空)。</summary>
    public string? ParentOrganizationNId { get; init; }

    /// <summary>同父节点内的显示顺序。</summary>
    public int DisplayOrder { get; init; }

    /// <summary>树修订号(结构变化递增,移动预览过期校验)。</summary>
    public long OrganizationRevision { get; init; }

    /// <summary>乐观版本(写接口乐观并发回传)。</summary>
    public long OptimisticVersion { get; init; }

    /// <summary>并发版本(写接口乐观并发回传)。</summary>
    public Guid ConcurrencyVersion { get; init; }
}

/// <summary>
/// 组织移动预览(GET/POST move-preview 响应,05 方案 §7.2):影响摘要与
/// 提交移动必须携带的 <see cref="OrganizationRevision"/> 与双并发版本。
/// </summary>
public sealed record OrganizationMovePreviewV1
{
    /// <summary>被移动组织业务标识。</summary>
    public string NId { get; init; } = string.Empty;

    /// <summary>移动成功后树修订号(当前修订 + 1)。</summary>
    public long OrganizationRevision { get; init; }

    /// <summary>待移动子树包含的组织数(含移动根)。</summary>
    public long SubtreeOrganizationCount { get; init; }

    /// <summary>待移动子树包含的岗位数。</summary>
    public long SubtreePositionCount { get; init; }

    /// <summary>待移动子树包含的任职数。</summary>
    public long SubtreeAssignmentCount { get; init; }

    /// <summary>影响总数(组织 + 岗位 + 任职)。</summary>
    public long AffectedCount { get; init; }

    /// <summary>预览生成时间。</summary>
    public DateTimeOffset PreviewedOn { get; init; }

    /// <summary>当前组织乐观版本(提交移动时回传)。</summary>
    public long ExpectedOptimisticVersion { get; init; }

    /// <summary>当前组织并发版本(提交移动时回传)。</summary>
    public Guid ExpectedConcurrencyVersion { get; init; }
}
