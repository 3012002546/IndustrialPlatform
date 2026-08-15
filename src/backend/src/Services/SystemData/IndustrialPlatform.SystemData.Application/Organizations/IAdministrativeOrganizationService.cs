using IndustrialPlatform.SystemData.Contracts.Administration;

namespace IndustrialPlatform.SystemData.Application.Organizations;

/// <summary>
/// 行政组织管理用例端口(TASK-SD-006,05 方案 §9.3 组织 API)。
/// 返回 Contracts DTO,响应不暴露数据库 Guid;跨租户 NId 统一 404(SD_NOT_FOUND)。
/// 写操作双版本/revision 冲突抛 <see cref="Administration.AdministrationConcurrencyConflictException"/>(409)。
/// </summary>
public interface IAdministrativeOrganizationService
{
    /// <summary>读取组织详情与并发版本(GET /organizations/{organizationNId});不存在 404。</summary>
    Task<OrganizationDetailV1> GetAsync(string tenantNId, string organizationNId, CancellationToken cancellationToken);

    /// <summary>读取组织森林(可按状态过滤),根公司集合作为森林根(GET /organizations/tree)。</summary>
    Task<IReadOnlyList<OrganizationNodeV1>> GetTreeAsync(string tenantNId, string? status, CancellationToken cancellationToken);

    /// <summary>创建根公司或子组织(POST /organizations)。</summary>
    Task<OrganizationDetailV1> CreateAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        CreateOrganizationRequest request,
        CancellationToken cancellationToken);

    /// <summary>修改组织名称、顺序(PUT /organizations/{organizationNId})。</summary>
    Task<OrganizationDetailV1> UpdateAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string organizationNId,
        UpdateOrganizationRequest request,
        CancellationToken cancellationToken);

    /// <summary>移动预览:返回影响摘要与提交移动必须携带的 revision/双版本(POST move-preview)。</summary>
    Task<OrganizationMovePreviewV1> PreviewMoveAsync(
        string tenantNId,
        string organizationNId,
        MoveOrganizationPreviewRequest request,
        CancellationToken cancellationToken);

    /// <summary>提交移动(POST /organizations/{organizationNId}/move);预览过期或版本不匹配 409。</summary>
    Task<OrganizationDetailV1> MoveAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string organizationNId,
        MoveOrganizationRequest request,
        CancellationToken cancellationToken);

    /// <summary>启用/停用组织(PUT /organizations/{organizationNId}/status);有活动依赖停用拒绝 409。</summary>
    Task<OrganizationDetailV1> SetStatusAsync(
        string tenantNId,
        string actorUserNId,
        string traceId,
        string organizationNId,
        SetOrganizationStatusRequest request,
        CancellationToken cancellationToken);
}
