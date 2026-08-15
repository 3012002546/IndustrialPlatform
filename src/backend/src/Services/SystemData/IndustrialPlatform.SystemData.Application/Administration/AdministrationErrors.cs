namespace IndustrialPlatform.SystemData.Application.Administration;

/// <summary>
/// 管理用例业务异常基类:携带标准 HTTP 状态码与 05 方案 §9.9 错误码。
/// 由 Api 控制器映射为统一 ApiResult 信封;message 只允许业务语义,
/// 不得包含 SQL、TraceId、Token 或用户目录完整响应。
/// </summary>
public abstract class AdministrationException : Exception
{
    /// <summary>标准 HTTP 状态码。</summary>
    public int StatusCode { get; }

    /// <summary>§9.9 错误码。</summary>
    public string Code { get; }

    protected AdministrationException(int statusCode, string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
    }
}

/// <summary>输入或组合规则不合法(§9.9 SD_VALIDATION_FAILED)。</summary>
public sealed class AdministrationValidationFailedException : AdministrationException
{
    public AdministrationValidationFailedException(string message)
        : base(400, "SD_VALIDATION_FAILED", message)
    {
    }
}

/// <summary>资源不存在或跨租户不可见(§9.9 SD_NOT_FOUND);不泄漏对象存在性。</summary>
public sealed class AdministrationNotFoundException : AdministrationException
{
    public AdministrationNotFoundException()
        : base(404, "SD_NOT_FOUND", "资源不存在。")
    {
    }
}

/// <summary>组织父子类型矩阵不允许(§9.9 SD_ORG_PARENT_TYPE_INVALID)。</summary>
public sealed class OrganizationParentTypeInvalidException : AdministrationException
{
    public OrganizationParentTypeInvalidException(string message)
        : base(400, "SD_ORG_PARENT_TYPE_INVALID", message)
    {
    }
}

/// <summary>移动将产生祖先循环(§9.9 SD_ORG_CYCLE)。</summary>
public sealed class OrganizationCycleException : AdministrationException
{
    public OrganizationCycleException()
        : base(409, "SD_ORG_CYCLE", "移动目标不能是待移动子树的成员,否则形成祖先循环。")
    {
    }
}

/// <summary>组织仍有活动依赖,不能停用(§9.9 SD_ORG_HAS_ACTIVE_DEPENDENCIES)。</summary>
public sealed class OrganizationHasActiveDependenciesException : AdministrationException
{
    public OrganizationHasActiveDependenciesException(int childCount, int positionCount, int assignmentCount)
        : base(
            409,
            "SD_ORG_HAS_ACTIVE_DEPENDENCIES",
            $"存在活动下级组织 {childCount} 个、活动岗位 {positionCount} 个、当前或未来有效任职 {assignmentCount} 条,不能停用组织。")
    {
    }
}

/// <summary>岗位仍有当前/未来有效任职,不能停用(§9.9 SD_POSITION_HAS_ACTIVE_ASSIGNMENTS)。</summary>
public sealed class PositionHasActiveAssignmentsException : AdministrationException
{
    public PositionHasActiveAssignmentsException()
        : base(409, "SD_POSITION_HAS_ACTIVE_ASSIGNMENTS", "岗位仍存在当前或未来有效任职,不能停用岗位。")
    {
    }
}

/// <summary>同一 (用户, 岗位) 存在区间重叠(§9.9 SD_ASSIGNMENT_INTERVAL_OVERLAP)。</summary>
public sealed class AssignmentIntervalOverlapException : AdministrationException
{
    public AssignmentIntervalOverlapException()
        : base(409, "SD_ASSIGNMENT_INTERVAL_OVERLAP", "该用户在同一岗位的任职区间与既有区间重叠。")
    {
    }
}

/// <summary>存在有效任职但缺少主任职(§9.9 SD_ASSIGNMENT_PRIMARY_REQUIRED)。</summary>
public sealed class AssignmentPrimaryRequiredException : AdministrationException
{
    public AssignmentPrimaryRequiredException()
        : base(409, "SD_ASSIGNMENT_PRIMARY_REQUIRED", "存在有效任职的时间段必须恰好有一个主任职,当前缺少主任职。")
    {
    }
}

/// <summary>同一时点存在多个主任职(§9.9 SD_ASSIGNMENT_PRIMARY_OVERLAP)。</summary>
public sealed class AssignmentPrimaryOverlapException : AdministrationException
{
    public AssignmentPrimaryOverlapException()
        : base(409, "SD_ASSIGNMENT_PRIMARY_OVERLAP", "同一时点存在多个主任职,请先结束或拆分既有主任职。")
    {
    }
}

/// <summary>双版本或 revision 冲突(§9.9 SD_CONCURRENCY_CONFLICT)。</summary>
public sealed class AdministrationConcurrencyConflictException : AdministrationException
{
    public AdministrationConcurrencyConflictException(string message)
        : base(409, "SD_CONCURRENCY_CONFLICT", message)
    {
    }
}

/// <summary>用户目录不可验证,新写入 fail-closed(§9.9 SD_IDENTITY_DIRECTORY_UNAVAILABLE)。</summary>
public sealed class IdentityDirectoryUnavailableException : AdministrationException
{
    public IdentityDirectoryUnavailableException(string message)
        : base(503, "SD_IDENTITY_DIRECTORY_UNAVAILABLE", message)
    {
    }
}
