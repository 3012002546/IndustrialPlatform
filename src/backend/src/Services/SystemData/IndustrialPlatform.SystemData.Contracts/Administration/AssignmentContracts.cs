namespace IndustrialPlatform.SystemData.Contracts.Administration;

// =====================================================================
// 用户任职公开契约(TASK-SD-006,05 方案 §9.3)。
// 约定与 OrganizationContracts 一致:请求可空、枚举名传输、不暴露数据库 Guid。
// 时间区间采用左闭右开 [EffectiveFrom, EffectiveTo);EffectiveTo 为空表示无界。
// =====================================================================

/// <summary>为用户创建任职请求(POST /users/{userNId}/assignments)。</summary>
public sealed record CreateAssignmentRequest
{
    /// <summary>任职业务标识(租户内唯一)。</summary>
    public string? NId { get; init; }

    /// <summary>任职岗位业务标识。</summary>
    public string? PositionNId { get; init; }

    /// <summary>是否主任职(同一用户同一活跃窗口内至多一个)。</summary>
    public bool? IsPrimary { get; init; }

    /// <summary>生效时间(UTC,左闭)。</summary>
    public DateTimeOffset? EffectiveFrom { get; init; }

    /// <summary>失效时间(UTC,右开;为空表示无界)。</summary>
    public DateTimeOffset? EffectiveTo { get; init; }
}

/// <summary>修改任职计划区间请求(PUT /assignments/{assignmentNId},仅 Scheduled)。</summary>
public sealed record UpdateScheduledAssignmentRequest
{
    /// <summary>生效时间(UTC,左闭)。</summary>
    public DateTimeOffset? EffectiveFrom { get; init; }

    /// <summary>失效时间(UTC,右开;为空表示无界)。</summary>
    public DateTimeOffset? EffectiveTo { get; init; }

    /// <summary>乐观版本(从 GET 用户任职列表读取)。</summary>
    public long? ExpectedOptimisticVersion { get; init; }

    /// <summary>并发版本(从 GET 用户任职列表读取)。</summary>
    public Guid? ExpectedConcurrencyVersion { get; init; }
}

/// <summary>取消计划中任职请求(POST /assignments/{assignmentNId}/cancel,仅 Scheduled)。</summary>
public sealed record CancelAssignmentRequest
{
    /// <summary>取消原因(可选,写入本地审计)。</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// 原子切换主任职请求(POST /users/{userNId}/primary-assignment)。
/// 在按用户 advisory lock 内执行:若存在 effectiveOn 时的主任职,先到期再指定新主任职。
/// </summary>
public sealed record SetPrimaryAssignmentRequest
{
    /// <summary>目标主任职业务标识。</summary>
    public string? TargetAssignmentNId { get; init; }

    /// <summary>切换生效时刻(UTC)。</summary>
    public DateTimeOffset? EffectiveOn { get; init; }

    /// <summary>原因(可选,写入本地审计)。</summary>
    public string? Reason { get; init; }

    /// <summary>当前主任职乐观版本(读-改-写竞态护栏,可选)。</summary>
    public long? ExpectedUserAssignmentRevision { get; init; }
}

/// <summary>任职查询项(GET /users/{userNId}/assignments)。</summary>
public sealed record AssignmentV1
{
    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; init; } = string.Empty;

    /// <summary>任职业务标识。</summary>
    public string NId { get; init; } = string.Empty;

    /// <summary>任职用户业务标识。</summary>
    public string UserNId { get; init; } = string.Empty;

    /// <summary>任职用户显示名快照(创建时取)。</summary>
    public string UserDisplayNameSnapshot { get; init; } = string.Empty;

    /// <summary>任职组织业务标识。</summary>
    public string OrganizationNId { get; init; } = string.Empty;

    /// <summary>任职岗位业务标识。</summary>
    public string PositionNId { get; init; } = string.Empty;

    /// <summary>任职岗位名称(响应丰富化)。</summary>
    public string PositionName { get; init; } = string.Empty;

    /// <summary>是否主任职。</summary>
    public bool IsPrimary { get; init; }

    /// <summary>生效时间(UTC,左闭)。</summary>
    public DateTimeOffset EffectiveFrom { get; init; }

    /// <summary>失效时间(UTC,右开;为空表示无界)。</summary>
    public DateTimeOffset? EffectiveTo { get; init; }

    /// <summary>任职状态枚举名(Scheduled/Current/Ended/Cancelled)。</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>取消时间(可选)。</summary>
    public DateTimeOffset? CancelledOn { get; init; }

    /// <summary>取消原因(可选)。</summary>
    public string? CancelReason { get; init; }

    /// <summary>乐观版本(写接口乐观并发回传)。</summary>
    public long OptimisticVersion { get; init; }

    /// <summary>并发版本(写接口乐观并发回传)。</summary>
    public Guid ConcurrencyVersion { get; init; }
}
