using System.Security.Claims;
using IndustrialPlatform.Security;
using IndustrialPlatform.SystemData.Application.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace IndustrialPlatform.SystemData.Api.Authorization;

/// <summary>
/// 权限授权处理器:优先兼容既有 <c>permission_nid</c> 声明；正常 Identity Token 不携带权限，
/// 改由权限裁决端口根据 sub/tenant_id/sid/ver 动态判定。
/// </summary>
public sealed class SystemDataPermissionAuthorizationHandler : AuthorizationHandler<SystemDataPermissionRequirement>
{
    internal const string DenialReasonItemsKey = "SystemData.Authorization.DenialReason";
    private readonly ISystemDataPermissionEvaluator _evaluator;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SystemDataPermissionAuthorizationHandler(
        ISystemDataPermissionEvaluator evaluator,
        IHttpContextAccessor httpContextAccessor)
    {
        _evaluator = evaluator;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SystemDataPermissionRequirement requirement)
    {
        var protectedInitializationPermission = IsInitializationPermission(requirement.PermissionNId);
        var granted = !protectedInitializationPermission && context.User
            .FindAll(SystemDataClaimTypes.PermissionNId)
            .SelectMany(ExpandPermissionClaim)
            .Contains(requirement.PermissionNId, StringComparer.Ordinal);

        if (granted)
        {
            context.Succeed(requirement);
            return;
        }

        var userNId = context.User.FindFirst(ClaimConstants.UserNId)?.Value;
        var tenantNId = context.User.FindFirst(ClaimConstants.TenantId)?.Value;
        var sessionNId = context.User.FindFirst(ClaimConstants.SessionId)?.Value;
        var version = context.User.FindFirst(ClaimConstants.AuthVersion)?.Value;
        if (string.IsNullOrWhiteSpace(userNId)
            || string.IsNullOrWhiteSpace(tenantNId)
            || !int.TryParse(version, out var authVersion))
        {
            if (_httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString()
                .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
            {
                RecordDenial(SystemDataPermissionDenialReason.SessionInvalid);
            }
            context.Fail();
            return;
        }

        var http = _httpContextAccessor.HttpContext;
        var authorization = http?.Request.Headers.Authorization.ToString();
        var accessToken = authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? authorization["Bearer ".Length..].Trim()
            : null;
        var decision = await _evaluator.EvaluateAsync(
            new SystemDataPermissionRequest(
                tenantNId,
                userNId,
                sessionNId,
                authVersion,
                requirement.PermissionNId,
                accessToken),
            http?.RequestAborted ?? CancellationToken.None);

        if (decision.Allowed)
        {
            context.Succeed(requirement);
            return;
        }

        RecordDenial(decision.Reason);
        context.Fail();
    }

    private void RecordDenial(SystemDataPermissionDenialReason reason)
    {
        if (_httpContextAccessor.HttpContext is { } http)
        {
            http.Items[DenialReasonItemsKey] = reason;
        }
    }

    /// <summary>展开单条声明值(空格分隔的权限 NId 列表)。</summary>
    private static IEnumerable<string> ExpandPermissionClaim(Claim claim) =>
        claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsInitializationPermission(string permissionNId) =>
        permissionNId.StartsWith("systemdata.service-initialization.", StringComparison.OrdinalIgnoreCase)
        || permissionNId.StartsWith("systemdata.database-orchestration.", StringComparison.OrdinalIgnoreCase);
}
