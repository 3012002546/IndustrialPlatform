using IndustrialPlatform.SystemData.Contracts.Administration;

namespace IndustrialPlatform.SystemData.Application.Assignments;

/// <summary>
/// 用户任职管理用例端口(TASK-SD-006,05 方案 §9.3 任职 API)。
/// 同一用户关键区按 advisory lock 串行执行,区间重叠/主任职覆盖经
/// <see cref="AssignmentScheduleRules"/> 裁决,结构化错误码 §9.9:
/// SD_ASSIGNMENT_INTERVAL_OVERLAP / SD_ASSIGNMENT_PRIMARY_REQUIRED /
/// SD_ASSIGNMENT_PRIMARY_OVERLAP / SD_CONCURRENCY_CONFLICT(409)。
/// 新写入要求用户目录可验证,否则 fail-closed 503(SD_IDENTITY_DIRECTORY_UNAVAILABLE)。
/// </summary>
public interface IUserAssignmentService
{
    /// <summary>查询用户全部任职(GET /users/{userNId}/assignments),投影状态按当前时间派生。</summary>
    Task<IReadOnlyList<AssignmentV1>> ListForUserAsync(string tenantNId, string userNId, CancellationToken cancellationToken);

    /// <summary>创建任职(POST /users/{userNId}/assignments)。</summary>
    Task<AssignmentV1> CreateAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string userNId,
        CreateAssignmentRequest request,
        CancellationToken cancellationToken);

    /// <summary>修改计划中任职区间(PUT /assignments/{assignmentNId},仅 Scheduled)。</summary>
    Task<AssignmentV1> UpdateScheduledAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string assignmentNId,
        UpdateScheduledAssignmentRequest request,
        CancellationToken cancellationToken);

    /// <summary>结束当前任职(POST /assignments/{assignmentNId}/end,仅 Current)。</summary>
    Task<AssignmentV1> EndAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string assignmentNId,
        CancellationToken cancellationToken);

    /// <summary>取消计划中任职(POST /assignments/{assignmentNId}/cancel,仅 Scheduled)。</summary>
    Task<AssignmentV1> CancelAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string assignmentNId,
        CancelAssignmentRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// 原子切换主任职(POST /users/{userNId}/primary-assignment):advisory lock 内
    /// 结束/拆分既有主任职并标记目标,返回切换后的完整时间线。
    /// </summary>
    Task<IReadOnlyList<AssignmentV1>> SetPrimaryAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string userNId,
        SetPrimaryAssignmentRequest request,
        CancellationToken cancellationToken);
}
