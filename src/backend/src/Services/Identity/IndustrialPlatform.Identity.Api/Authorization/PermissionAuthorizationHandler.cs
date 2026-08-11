using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Security;
using Microsoft.AspNetCore.Authorization;

namespace IndustrialPlatform.Identity.Api.Authorization;

/// <summary>
/// 权限授权处理器(§18):从认证用户声明读取 sub/tenant_id/sid/ver,
/// 调用 <see cref="IPermissionEvaluator"/> 裁决。拒绝原因与存储不可用写入
/// <see cref="HttpContext.Items"/> 供 JwtBearer OnForbidden 映射 401/403/503 信封;
/// 拒绝审计由评估器负责。
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    /// <summary>HttpContext.Items 中拒绝原因键,OnForbidden 据此映射信封。</summary>
    internal const string DenialReasonItemsKey = "Identity.Authorization.DenialReason";

    private readonly IPermissionEvaluator _evaluator;
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>初始化权限授权处理器。</summary>
    public PermissionAuthorizationHandler(
        IPermissionEvaluator evaluator,
        IHttpContextAccessor httpContextAccessor)
    {
        _evaluator = evaluator;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc/>
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userNId = context.User.FindFirst(ClaimConstants.UserNId)?.Value;
        var tenantNId = context.User.FindFirst(ClaimConstants.TenantId)?.Value;
        var sessionNId = context.User.FindFirst(ClaimConstants.SessionId)?.Value;
        var ver = context.User.FindFirst(ClaimConstants.AuthVersion)?.Value;

        if (string.IsNullOrWhiteSpace(userNId)
            || string.IsNullOrWhiteSpace(tenantNId)
            || !int.TryParse(ver, out var authVersion))
        {
            RecordDenial(context, AuthorizationDenialReason.SessionInvalid);
            context.Fail();
            return;
        }

        PermissionEvaluation evaluation;
        try
        {
            evaluation = await _evaluator.EvaluateAsync(
                tenantNId,
                userNId,
                sessionNId,
                authVersion,
                requirement.PermissionNId,
                RequestAborted);
        }
        catch (SecurityStoreUnavailableException)
        {
            // 撤销/授权数据存储不可用 → fail-closed 503,由 OnForbidden 映射信封。
            RecordDenial(context, AuthorizationDenialReason.SecurityStoreUnavailable);
            context.Fail();
            return;
        }

        if (evaluation.Allowed)
        {
            context.Succeed(requirement);
            return;
        }

        RecordDenial(context, evaluation.Reason);
        context.Fail();
    }

    private HttpContext? CurrentHttpContext => _httpContextAccessor.HttpContext;

    private CancellationToken RequestAborted => CurrentHttpContext?.RequestAborted ?? CancellationToken.None;

    private void RecordDenial(AuthorizationHandlerContext context, AuthorizationDenialReason reason)
    {
        if (CurrentHttpContext is { } http)
        {
            http.Items[DenialReasonItemsKey] = reason;
        }
    }
}
