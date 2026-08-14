using IndustrialPlatform.Security;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.SystemData.Api.Controllers;

/// <summary>
/// 数据库编排控制器基类(05 方案 §9.2):统一从当前用户上下文解析租户与执行者标识
/// (SD-006 接入鉴权后由授权管线保证声明有效,当前为防御分支),提供标准 ApiResult
/// 信封助手。租户编码只从当前用户上下文读取,绝不信任请求体传入。
/// </summary>
public abstract class SystemDataControllerBase : ControllerBase
{
    private readonly ICurrentUser _currentUser;

    /// <summary>初始化编排控制器基类。</summary>
    protected SystemDataControllerBase(ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        _currentUser = currentUser;
    }

    /// <summary>
    /// 从当前用户上下文读取租户与执行者业务标识;缺失时返回 <c>false</c>
    /// (授权管线外的防御分支,SD-006 接入后正常路径由鉴权保证声明有效)。
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

    /// <summary>携带数据负载的指定状态码信封(如 readiness 未就绪 503 仍携带 DatabaseReadinessV1 形状)。</summary>
    protected static ObjectResult StatusCodeEnvelope<T>(int statusCode, string code, string message, T data)
    {
        var failure = ApiResult.Fail<T>(code, message);
        failure.Data = data;
        return new ObjectResult(failure) { StatusCode = statusCode };
    }

    /// <summary>异步入队结果信封(202 Accepted):ResultFilter 对 200-399 自动包 ApiResult,显式包装保持统一形状。</summary>
    protected static ObjectResult AcceptedEnvelope<T>(T value) =>
        new(ApiResult.Ok(value)) { StatusCode = StatusCodes.Status202Accepted };
}
