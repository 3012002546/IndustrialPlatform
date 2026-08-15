using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Security;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.Identity.Api.Controllers;

/// <summary>
/// 管理控制器基类(§16):统一从令牌解析租户与执行者上下文(授权管线已保证
/// sub/tenant_id 声明有效),提供标准 ApiResult 错误信封助手与写请求幂等助手(§29A.5)。
/// 租户编码只从令牌读取,绝不信任请求体传入(§18)。
/// </summary>
public abstract class ManagementControllerBase : ControllerBase
{
    private readonly ICurrentUser _currentUser;
    private readonly IIdempotencyStore _idempotencyStore;

    /// <summary>初始化管理控制器基类。</summary>
    protected ManagementControllerBase(ICurrentUser currentUser, IIdempotencyStore idempotencyStore)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(idempotencyStore);
        _currentUser = currentUser;
        _idempotencyStore = idempotencyStore;
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

    /// <summary>
    /// 以 Idempotency-Key 执行写操作(§29A.5):携带键时记录请求哈希并校验冲突;
    /// 未携带键时直接执行(兼容既有客户端)。同键同内容且已完成的重放返回 <c>null</c>
    /// (调用方返回幂等确认),同键不同内容抛 <see cref="IdempotencyConflictException"/>。
    /// </summary>
    protected async Task<T?> ExecuteIdempotentAsync<T>(
        string tenantNId,
        string actorUserNId,
        object request,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        var key = ReadIdempotencyKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            return await action();
        }

        var requestHash = ComputeRequestHash(request);
        var decision = await _idempotencyStore.TryReserveAsync(tenantNId, actorUserNId, key, requestHash, cancellationToken);
        if (decision == IdempotencyDecision.Replay)
        {
            return default;
        }

        try
        {
            var result = await action();
            await _idempotencyStore.MarkCompletedAsync(tenantNId, actorUserNId, key, cancellationToken);
            return result;
        }
        catch
        {
            await _idempotencyStore.ReleaseAsync(tenantNId, actorUserNId, key, cancellationToken);
            throw;
        }
    }

    /// <summary>读取 Idempotency-Key 请求头(取首个值,缺失返回 null)。</summary>
    private string? ReadIdempotencyKey() =>
        HttpContext.Request.Headers.TryGetValue("Idempotency-Key", out var values) && values.Count > 0
            ? values[0]
            : null;

    /// <summary>请求内容 SHA-256 哈希(规范化 JSON,不含请求体明文)。</summary>
    private static string ComputeRequestHash(object request)
    {
        var json = JsonSerializer.Serialize(request, RequestHashJsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static readonly JsonSerializerOptions RequestHashJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    protected static ObjectResult UnauthorizedEnvelope() =>
        StatusCodeEnvelope(StatusCodes.Status401Unauthorized, "401", "登录已失效，请重新登录。");

    protected static ObjectResult OkEnvelope() =>
        new(ApiResult.Ok()) { StatusCode = StatusCodes.Status200OK };

    protected static ObjectResult BadRequestEnvelope(string code, string message) =>
        StatusCodeEnvelope(StatusCodes.Status400BadRequest, code, message);

    protected static ObjectResult StatusCodeEnvelope(int statusCode, string code, string message) =>
        new(ApiResult.Fail<object?>(code, message)) { StatusCode = statusCode };
}
