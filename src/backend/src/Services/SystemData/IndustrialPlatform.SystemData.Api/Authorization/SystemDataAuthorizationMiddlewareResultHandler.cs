using IndustrialPlatform.SystemData.Application.Authorization;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace IndustrialPlatform.SystemData.Api.Authorization;

/// <summary>把 SystemData 动态权限裁决结果映射为稳定的 401/403/503 信封。</summary>
public sealed class SystemDataAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _fallback = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Forbidden
            || !context.Items.TryGetValue(SystemDataPermissionAuthorizationHandler.DenialReasonItemsKey, out var value)
            || value is not SystemDataPermissionDenialReason reason)
        {
            await _fallback.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var (status, code, message) = reason switch
        {
            SystemDataPermissionDenialReason.SessionInvalid =>
                (StatusCodes.Status401Unauthorized, "401", "登录已失效，请重新登录。"),
            SystemDataPermissionDenialReason.SecurityStoreUnavailable =>
                (StatusCodes.Status503ServiceUnavailable, "SD_AUTHORIZATION_UNAVAILABLE", "权限服务暂时不可用，请稍后再试。"),
            _ =>
                (StatusCodes.Status403Forbidden, "SD_PERMISSION_DENIED", "无权限执行此操作。"),
        };
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(ApiResult.Fail<object?>(code, message));
    }
}
