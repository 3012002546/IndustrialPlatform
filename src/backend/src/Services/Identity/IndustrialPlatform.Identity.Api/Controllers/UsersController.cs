using IndustrialPlatform.Identity.Api.Authorization;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Contracts.Management;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Security;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.Identity.Api.Controllers;

/// <summary>
/// 用户管理端点(§16.1):分页/详情/创建/修改/状态/角色分配/密码重置。
/// 写操作按乐观并发,跨租户资源统一 404;密码重置撤销目标用户全部会话。
/// </summary>
[ApiController]
[Route("users")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UsersController : ManagementControllerBase
{
    private readonly IUserManagementService _service;

    /// <summary>初始化用户管理控制器。</summary>
    public UsersController(IUserManagementService service, ICurrentUser currentUser, IIdempotencyStore idempotencyStore)
        : base(currentUser, idempotencyStore)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>分页查询用户(§16.1/§29A.5 GET /api/v1/users),NId/LoginName/Name 包含匹配,Status/GroupNId/RoleNId 可选过滤;includeDeleted 返回墓碑(§29A.3)。</summary>
    [Authorize(Policy = PermissionPolicies.UserView)]
    [HttpGet]
    public async Task<ActionResult<PageResult<UserSummary>>> List(
        [FromQuery] string? nId,
        [FromQuery] string? loginName,
        [FromQuery] string? name,
        [FromQuery] string? status,
        [FromQuery] string? groupNId,
        [FromQuery] string? roleNId,
        [FromQuery] bool includeDeleted = false,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        UserStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<UserStatus>(status, ignoreCase: true, out var parsed))
            {
                return BadRequestEnvelope("ID_VALIDATION_FAILED", "无效的用户状态过滤条件。");
            }

            parsedStatus = parsed;
        }

        if (pageIndex < 1 || pageSize is < 1 or > 100)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", "分页参数无效。");
        }

        try
        {
            var page = await _service.ListAsync(
                tenantNId,
                new UserListFilter(tenantNId, nId, loginName, name, parsedStatus, pageIndex, pageSize, includeDeleted, groupNId, roleNId),
                cancellationToken);
            return PageResult.Create(page.Items, page.Total, page.PageIndex, page.PageSize);
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

    /// <summary>用户详情(§16.1 GET /api/v1/users/{id});不存在或跨租户返回 404。</summary>
    [Authorize(Policy = PermissionPolicies.UserView)]
    [HttpGet("{id}")]
    public async Task<ActionResult<UserSummary>> Get(string id, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _service.GetAsync(tenantNId, id, cancellationToken);
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

    /// <summary>创建用户(§16.1/§29A.4 POST /api/v1/users);NId/登录名冲突 409;服务端生成随机临时密码只在本次 201 响应出现一次。</summary>
    [Authorize(Policy = PermissionPolicies.UserCreate)]
    [HttpPost]
    public async Task<ActionResult<CreateUserResult>> Create(CreateUserRequest request, CancellationToken cancellationToken)
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
                () => _service.CreateAsync(tenantNId, actorUserNId, request, cancellationToken),
                cancellationToken);
            if (result is null)
            {
                // 同 Idempotency-Key 同内容重放:已成功,返回幂等确认。
                return OkEnvelope();
            }

            return result;
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

    /// <summary>修改用户资料/登录名(§16.1 PUT /api/v1/users/{id});乐观并发 409。</summary>
    [Authorize(Policy = PermissionPolicies.UserUpdate)]
    [HttpPut("{id}")]
    public async Task<ActionResult<UserSummary>> Update(
        string id,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _service.UpdateAsync(tenantNId, actorUserNId, id, request, cancellationToken);
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

    /// <summary>启用/禁用用户(§16.1 PUT /api/v1/users/{id}/status);禁止禁用自己或最后一名系统管理员。</summary>
    [Authorize(Policy = PermissionPolicies.UserStatus)]
    [HttpPut("{id}/status")]
    public async Task<ActionResult<UserSummary>> SetStatus(
        string id,
        SetUserStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _service.SetStatusAsync(tenantNId, actorUserNId, id, request, cancellationToken);
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

    /// <summary>分配角色(§16.1 PUT /api/v1/users/{id}/roles);差量更新并失效持有者权限缓存。</summary>
    [Authorize(Policy = PermissionPolicies.UserAssignRole)]
    [HttpPut("{id}/roles")]
    public async Task<ActionResult<UserSummary>> AssignRoles(
        string id,
        AssignUserRolesRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _service.AssignRolesAsync(tenantNId, actorUserNId, id, request, cancellationToken);
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

    /// <summary>管理员重置密码(§29A.4/§29A.5 POST /api/v1/users/{id}/reset-password);服务端生成随机临时密码只返回一次,强制改密并撤销全部会话。</summary>
    [Authorize(Policy = PermissionPolicies.UserResetPassword)]
    [HttpPost("{id}/reset-password")]
    public async Task<ActionResult<ResetPasswordResult>> ResetPassword(
        string id,
        ResetPasswordRequest request,
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
                () => _service.ResetPasswordAsync(tenantNId, actorUserNId, id, request, cancellationToken),
                cancellationToken);
            if (result is null)
            {
                // 同 Idempotency-Key 同内容重放:已成功,返回幂等确认。
                return OkEnvelope();
            }

            return result;
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

    /// <summary>安全删除用户(墓碑,§29A.3 DELETE /api/v1/users/{id});禁删自己/内置 ADMIN/最后一名系统管理员,撤销全部会话。</summary>
    [Authorize(Policy = PermissionPolicies.UserDelete)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(
        string id,
        DeleteUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            await _service.DeleteAsync(tenantNId, actorUserNId, id, request, cancellationToken);
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

    /// <summary>恢复用户墓碑(§29A.3 POST /api/v1/users/{id}/restore);仅恢复为 Disabled,不自动恢复授权。</summary>
    [Authorize(Policy = PermissionPolicies.UserRestore)]
    [HttpPost("{id}/restore")]
    public async Task<ActionResult<UserSummary>> Restore(
        string id,
        RestoreUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _service.RestoreAsync(tenantNId, actorUserNId, id, request, cancellationToken);
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
}
