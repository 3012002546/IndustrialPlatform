using IndustrialPlatform.SystemData.Application.Administration;
using IndustrialPlatform.SystemData.Contracts.Administration;

namespace IndustrialPlatform.SystemData.Application.Positions;

/// <summary>
/// 岗位管理用例端口(TASK-SD-006,05 方案 §9.3 岗位 API)。
/// 岗位列表按组织/状态分页;创建后所属组织不可修改;岗位不可移动。
/// 停用有当前/未来任职的岗位抛 <see cref="Administration.PositionHasActiveAssignmentsException"/>(409)。
/// </summary>
public interface IPositionService
{
    /// <summary>读取岗位详情(PUT/POST 响应携带双并发版本供后续写)。</summary>
    Task<PositionV1> GetAsync(string tenantNId, string positionNId, CancellationToken cancellationToken);

    /// <summary>按组织/状态分页查询岗位(GET /positions)。</summary>
    Task<AdministrationPageResult<PositionV1>> ListAsync(
        string tenantNId,
        string? organizationNId,
        string? status,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>创建岗位(POST /positions)。</summary>
    Task<PositionV1> CreateAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        CreatePositionRequest request,
        CancellationToken cancellationToken);

    /// <summary>修改岗位名称、描述、顺序(PUT /positions/{positionNId})。</summary>
    Task<PositionV1> UpdateAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string positionNId,
        UpdatePositionRequest request,
        CancellationToken cancellationToken);

    /// <summary>启用/停用岗位(PUT /positions/{positionNId}/status);有当前/未来任职停用拒绝 409。</summary>
    Task<PositionV1> SetStatusAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string positionNId,
        SetPositionStatusRequest request,
        CancellationToken cancellationToken);
}
