namespace IndustrialPlatform.SystemData.Contracts.Administration;

// =====================================================================
// 岗位公开契约(TASK-SD-006,05 方案 §9.3)。
// 约定与 OrganizationContracts 一致:请求可空、枚举名传输、不暴露数据库 Guid。
// =====================================================================

/// <summary>创建岗位请求(POST /positions)。</summary>
public sealed record CreatePositionRequest
{
    /// <summary>岗位业务标识(租户内唯一)。</summary>
    public string? NId { get; init; }

    /// <summary>所属组织业务标识。</summary>
    public string? OrganizationNId { get; init; }

    /// <summary>岗位名称(组织内大小写不敏感唯一)。</summary>
    public string? Name { get; init; }

    /// <summary>岗位描述(可选)。</summary>
    public string? Description { get; init; }

    /// <summary>同组织内的显示顺序(非负)。</summary>
    public int? DisplayOrder { get; init; }
}

/// <summary>修改岗位请求(PUT /positions/{positionNId})。</summary>
public sealed record UpdatePositionRequest
{
    /// <summary>岗位名称。</summary>
    public string? Name { get; init; }

    /// <summary>岗位描述(可选)。</summary>
    public string? Description { get; init; }

    /// <summary>同组织内的显示顺序(非负)。</summary>
    public int? DisplayOrder { get; init; }

    /// <summary>乐观版本(从 GET 详情读取)。</summary>
    public long? ExpectedOptimisticVersion { get; init; }

    /// <summary>并发版本(从 GET 详情读取)。</summary>
    public Guid? ExpectedConcurrencyVersion { get; init; }
}

/// <summary>启用/停用岗位请求(PUT /positions/{positionNId}/status)。</summary>
public sealed record SetPositionStatusRequest
{
    /// <summary>目标状态枚举名(Active/Inactive)。</summary>
    public string? Status { get; init; }

    /// <summary>原因(可选,写入本地审计)。</summary>
    public string? Reason { get; init; }
}

/// <summary>岗位查询项(GET /positions 分页列表,以及详情响应复用)。</summary>
public sealed record PositionV1
{
    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; init; } = string.Empty;

    /// <summary>岗位业务标识。</summary>
    public string NId { get; init; } = string.Empty;

    /// <summary>所属组织业务标识。</summary>
    public string OrganizationNId { get; init; } = string.Empty;

    /// <summary>所属组织名称(响应丰富化)。</summary>
    public string OrganizationName { get; init; } = string.Empty;

    /// <summary>岗位名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>岗位描述。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>岗位状态枚举名。</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>同组织内的显示顺序。</summary>
    public int DisplayOrder { get; init; }

    /// <summary>乐观版本(写接口乐观并发回传)。</summary>
    public long OptimisticVersion { get; init; }

    /// <summary>并发版本(写接口乐观并发回传)。</summary>
    public Guid ConcurrencyVersion { get; init; }
}
