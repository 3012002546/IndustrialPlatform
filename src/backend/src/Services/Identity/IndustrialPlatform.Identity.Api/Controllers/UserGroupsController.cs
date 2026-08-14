using IndustrialPlatform.Identity.Api.Authorization;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Application.UserGroups;
using IndustrialPlatform.Identity.Contracts.Management;
using IndustrialPlatform.Security;
using IndustrialPlatform.SharedKernel.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.Identity.Api.Controllers;

/// <summary>
/// 用户组安全删除/恢复端点(§29A.3/§29A.5):安全软删除墓碑与受控恢复。
/// 用户组完整管理契约(查询/创建/编辑/状态/成员/角色)由 TASK-ID-020 补齐。
/// </summary>
[ApiController]
[Route("user-groups")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UserGroupsController : ManagementControllerBase
{
    private readonly IUserGroupService _service;

    /// <summary>初始化用户组生命周期控制器。</summary>
    public UserGroupsController(IUserGroupService service, ICurrentUser currentUser)
        : base(currentUser)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
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
            await _service.DeleteAsync(
                tenantNId,
                actorUserNId,
                id,
                request.ExpectedOptimisticVersion,
                request.ExpectedConcurrencyVersion,
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
    public async Task<ActionResult<UserGroupSummary>> Restore(
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
            return await _service.RestoreAsync(
                tenantNId,
                actorUserNId,
                id,
                request.ExpectedOptimisticVersion,
                request.ExpectedConcurrencyVersion,
                cancellationToken);
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
