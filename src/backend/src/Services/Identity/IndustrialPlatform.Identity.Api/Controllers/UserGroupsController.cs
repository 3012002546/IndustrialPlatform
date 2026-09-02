using IndustrialPlatform.Identity.Api.Export;
using IndustrialPlatform.Identity.Api.Authorization;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Application.UserGroups;
using IndustrialPlatform.Identity.Contracts.Management;
using IndustrialPlatform.Identity.Domain.UserGroups;
using IndustrialPlatform.Security;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.Identity.Api.Controllers;

/// <summary>
/// 用户组管理端点(§29A.5):分页/详情/创建/编辑/启停/成员集/角色集/安全删除与恢复。
/// 写操作按双版本乐观并发并支持 Idempotency-Key 幂等;跨租户资源统一 404。
/// </summary>
[ApiController]
[Route("user-groups")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UserGroupsController : ManagementControllerBase
{
    private readonly IUserGroupService _service;

    /// <summary>初始化用户组管理控制器。</summary>
    public UserGroupsController(IUserGroupService service, ICurrentUser currentUser, IIdempotencyStore idempotencyStore)
        : base(currentUser, idempotencyStore)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>分页查询用户组(§29A.5 GET /api/v1/user-groups),Name 包含匹配,Status 可选过滤;includeDeleted 返回墓碑(§29A.3)。</summary>
    [Authorize(Policy = PermissionPolicies.UserGroupView)]
    [HttpGet]
    public async Task<ActionResult<PageResult<UserGroupSummaryDto>>> List(
        [FromQuery] string? nId,
        [FromQuery] string? name,
        [FromQuery] string? description,
        [FromQuery] string? status,
        [FromQuery] string? keyword,
        [FromQuery] bool includeDeleted = false,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? sortField = null,
        [FromQuery] string? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        if (pageIndex < 1 || pageSize is < 1 or > 100)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", "分页参数无效。");
        }

        UserGroupStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<UserGroupStatus>(status, ignoreCase: true, out var parsed))
            {
                return BadRequestEnvelope("ID_VALIDATION_FAILED", "无效的用户组状态过滤条件。");
            }

            parsedStatus = parsed;
        }

        try
        {
            var page = await _service.ListAsync(
                tenantNId,
                new UserGroupListQuery(tenantNId, name, parsedStatus, pageIndex, pageSize, includeDeleted, sortField, sortOrder, nId, description, keyword),
                cancellationToken);
            return PageResult.Create(page.Items.Select(ToSummaryDto).ToList(), page.Total, page.PageIndex, page.PageSize);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    [Authorize(Policy = PermissionPolicies.UserGroupView)]
    [HttpGet("export")]
    public IActionResult Export(
        [FromQuery] string? nId,
        [FromQuery] string? name,
        [FromQuery] string? description,
        [FromQuery] string? status,
        [FromQuery] string? keyword,
        [FromQuery] bool includeDeleted = false,
        [FromQuery] string quantity = "10000",
        [FromQuery] string? columns = null,
        [FromQuery] string? sortField = null,
        [FromQuery] string? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActorContext(out var tenantNId, out _)) return UnauthorizedEnvelope();
        UserGroupStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<UserGroupStatus>(status, true, out var parsed)) parsedStatus = parsed;
        var maxRows = StreamingXlsxExport.ParseQuantity(quantity);
        return StreamingXlsxExport.Start("user-groups.xlsx", async (output, ct) =>
        {
            var workbook = await StreamingXlsxWriter.CreateAsync(output, ct);
            var selectedColumns = StreamingXlsxExport.SelectColumns(columns,
                new StreamingXlsxExport.Column<StoredUserGroup>("groupNId", "用户组标识", group => group.NId),
                new StreamingXlsxExport.Column<StoredUserGroup>("name", "名称", group => group.Name),
                new StreamingXlsxExport.Column<StoredUserGroup>("description", "描述", group => group.Description),
                new StreamingXlsxExport.Column<StoredUserGroup>("status", "状态", group => group.Status),
                new StreamingXlsxExport.Column<StoredUserGroup>("memberCount", "成员数", group => group.MemberCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new StreamingXlsxExport.Column<StoredUserGroup>("roleCount", "角色数", group => group.RoleCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            await workbook.WriteRowAsync(selectedColumns.Select(column => column.Title), ct);
            var written = 0;
            var pageIndex = 1;
            while (written < maxRows)
            {
                var page = await _service.ListAsync(tenantNId, new UserGroupListQuery(tenantNId, name, parsedStatus, pageIndex, 100, includeDeleted, sortField, sortOrder, nId, description, keyword), ct);
                if (page.Items.Count == 0) break;
                foreach (var group in page.Items)
                {
                    if (written++ >= maxRows) break;
                    await workbook.WriteRowAsync(selectedColumns.Select(column => column.Value(group)), ct);
                }
                if (written >= page.Total || page.Items.Count < 100) break;
                pageIndex++;
            }
            await workbook.DisposeAsync();
        }, cancellationToken);
    }

    /// <summary>用户组详情(§29A.5 GET /api/v1/user-groups/{id});不存在或跨租户返回 404。</summary>
    [Authorize(Policy = PermissionPolicies.UserGroupView)]
    [HttpGet("{id}")]
    public async Task<ActionResult<UserGroupDetailDto>> Get(string id, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return ToDetailDto(await _service.GetAsync(tenantNId, id, cancellationToken));
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>创建用户组(§29A.5 POST /api/v1/user-groups);可原子提交初始成员与角色;NId 冲突 409。</summary>
    [Authorize(Policy = PermissionPolicies.UserGroupCreate)]
    [HttpPost]
    public async Task<ActionResult<UserGroupDetailDto>> Create(CreateUserGroupRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            var command = new CreateUserGroupCommand(
                request.NId,
                request.Name ?? string.Empty,
                request.Description,
                request.MemberUserNIds ?? [],
                request.RoleNIds ?? []);
            var result = await ExecuteIdempotentAsync(
                tenantNId,
                actorUserNId,
                request,
                () => _service.CreateAsync(tenantNId, actorUserNId, command, cancellationToken),
                cancellationToken);
            if (result is null)
            {
                return OkEnvelope();
            }

            return ToDetailDto(result);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>修改用户组名称/描述(§29A.5 PUT /api/v1/user-groups/{id});NId 不可修改,乐观并发 409。</summary>
    [Authorize(Policy = PermissionPolicies.UserGroupUpdate)]
    [HttpPut("{id}")]
    public async Task<ActionResult<UserGroupDetailDto>> Update(
        string id,
        UpdateUserGroupRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            var command = new UpdateUserGroupCommand(
                request.Name ?? string.Empty,
                request.Description,
                request.ExpectedOptimisticVersion,
                request.ExpectedConcurrencyVersion);
            var result = await ExecuteIdempotentAsync(
                tenantNId,
                actorUserNId,
                request,
                () => _service.UpdateAsync(tenantNId, actorUserNId, id, command, cancellationToken),
                cancellationToken);
            if (result is null)
            {
                return OkEnvelope();
            }

            return ToDetailDto(result);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>启用/禁用用户组(§29A.5 PUT /api/v1/user-groups/{id}/status);禁用失效全部成员授权并守卫最后系统管理员。</summary>
    [Authorize(Policy = PermissionPolicies.UserGroupStatus)]
    [HttpPut("{id}/status")]
    public async Task<ActionResult<UserGroupDetailDto>> SetStatus(
        string id,
        SetUserGroupStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            var result = await ExecuteIdempotentAsync(
                tenantNId,
                actorUserNId,
                request,
                () => _service.SetStatusAsync(
                    tenantNId,
                    actorUserNId,
                    id,
                    request.Enabled,
                    request.ExpectedOptimisticVersion,
                    request.ExpectedConcurrencyVersion,
                    cancellationToken),
                cancellationToken);
            if (result is null)
            {
                return OkEnvelope();
            }

            return ToDetailDto(result);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>设置最终成员集(§29A.5 PUT /api/v1/user-groups/{id}/members);幂等收敛,最后系统管理员守卫。</summary>
    [Authorize(Policy = PermissionPolicies.UserGroupAssignMember)]
    [HttpPut("{id}/members")]
    public async Task<ActionResult<UserGroupDetailDto>> SetMembers(
        string id,
        SetUserGroupMembersRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            var result = await ExecuteIdempotentAsync(
                tenantNId,
                actorUserNId,
                request,
                () => _service.SetMembersAsync(
                    tenantNId,
                    actorUserNId,
                    id,
                    request.MemberUserNIds ?? [],
                    request.ExpectedOptimisticVersion,
                    request.ExpectedConcurrencyVersion,
                    cancellationToken),
                cancellationToken);
            if (result is null)
            {
                return OkEnvelope();
            }

            return ToDetailDto(result);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>设置最终角色集(§29A.5 PUT /api/v1/user-groups/{id}/roles);幂等收敛,不接受权限。</summary>
    [Authorize(Policy = PermissionPolicies.UserGroupAssignRole)]
    [HttpPut("{id}/roles")]
    public async Task<ActionResult<UserGroupDetailDto>> SetRoles(
        string id,
        SetUserGroupRolesRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            var result = await ExecuteIdempotentAsync(
                tenantNId,
                actorUserNId,
                request,
                () => _service.SetRolesAsync(
                    tenantNId,
                    actorUserNId,
                    id,
                    request.RoleNIds ?? [],
                    request.ExpectedOptimisticVersion,
                    request.ExpectedConcurrencyVersion,
                    cancellationToken),
                cancellationToken);
            if (result is null)
            {
                return OkEnvelope();
            }

            return ToDetailDto(result);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>安全删除用户组(§29A.5 DELETE /api/v1/user-groups/{id});软删并解除有效关系,最后系统管理员守卫。</summary>
    [Authorize(Policy = PermissionPolicies.UserGroupDelete)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(
        string id,
        DeleteUserGroupRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            await ExecuteIdempotentAsync(
                tenantNId,
                actorUserNId,
                request,
                async () =>
                {
                    await _service.DeleteAsync(
                        tenantNId,
                        actorUserNId,
                        id,
                        request.ExpectedOptimisticVersion,
                        request.ExpectedConcurrencyVersion,
                        cancellationToken);
                    return true;
                },
                cancellationToken);
            return OkEnvelope();
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>恢复用户组墓碑(§29A.5 POST /api/v1/user-groups/{id}/restore);仅恢复为 Disabled,不自动恢复关系。</summary>
    [Authorize(Policy = PermissionPolicies.UserGroupRestore)]
    [HttpPost("{id}/restore")]
    public async Task<ActionResult<UserGroupDetailDto>> Restore(
        string id,
        RestoreUserGroupRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            var result = await ExecuteIdempotentAsync(
                tenantNId,
                actorUserNId,
                request,
                () => _service.RestoreAsync(
                    tenantNId,
                    actorUserNId,
                    id,
                    request.ExpectedOptimisticVersion,
                    request.ExpectedConcurrencyVersion,
                    cancellationToken),
                cancellationToken);
            if (result is null)
            {
                return OkEnvelope();
            }

            return ToDetailDto(result);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    private static UserGroupSummaryDto ToSummaryDto(StoredUserGroup g) => new(
        g.NId,
        g.Name,
        g.Description,
        g.Status,
        g.MemberCount,
        g.RoleCount,
        g.OptimisticVersion,
        g.ConcurrencyVersion,
        g.IsDeleted);

    private static UserGroupDetailDto ToDetailDto(UserGroupSummary g) => new(
        g.NId,
        g.Name,
        g.Description,
        g.Status,
        g.TenantNId,
        g.MemberUserNIds,
        g.RoleNIds,
        g.OptimisticVersion,
        g.ConcurrencyVersion);
}
