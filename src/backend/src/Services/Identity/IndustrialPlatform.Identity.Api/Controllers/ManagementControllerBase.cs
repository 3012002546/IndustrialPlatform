using IndustrialPlatform.Security;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.Identity.Api.Controllers;

/// <summary>
/// 管理控制器基类(§16):统一从令牌解析租户与执行者上下文(授权管线已保证
/// sub/tenant_id 声明有效),提供标准 ApiResult 错误信封助手。租户编码只从令牌读取,
/// 绝不信任请求体传入(§18)。
/// </summary>
public abstract class ManagementControllerBase : ControllerBase
{
    private readonly ICurrentUser _currentUser;

    /// <summary>初始化管理控制器基类。</summary>
    protected ManagementControllerBase(ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        _currentUser = currentUser;
    }

    /// <summary>
    /// 从令牌读取租户与执行者业务标识;缺失时返回 <c>false</c>(授权管线外的防御分支,
    /// 正常路径下 [Authorize(Policy)] 已保证声明有效)。
    /// </summary>
    protected bool TryGetActorContext(out string tenantNId, out string actorUserNId)
    {
        tenantNId = _currentUser.TenantId ?? string.Empty;
        actorUserNId = _currentUser.UserNId ?? string.Empty;
        return !string.IsNullOrWhiteSpace(tenantNId) && !string.IsNullOrWhiteSpace(actorUserNId);
    }

    protected static ObjectResult UnauthorizedEnvelope() =>
        StatusCodeEnvelope(StatusCodes.Status401Unauthorized, "401", "登录已失效，请重新登录。");

    protected static ObjectResult OkEnvelope() =>
        new(ApiResult.Ok()) { StatusCode = StatusCodes.Status200OK };

    protected static ObjectResult BadRequestEnvelope(string code, string message) =>
        StatusCodeEnvelope(StatusCodes.Status400BadRequest, code, message);

    protected static ObjectResult StatusCodeEnvelope(int statusCode, string code, string message) =>
        new(ApiResult.Fail<object?>(code, message)) { StatusCode = statusCode };
}
