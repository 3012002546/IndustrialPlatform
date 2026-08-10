using System.Globalization;
using System.Text.Json;
using IndustrialPlatform.Web.Results;
using Yarp.ReverseProxy.Forwarder;

namespace IndustrialPlatform.Gateway.Errors;

/// <summary>
/// 将 YARP 转发错误统一转换为 <see cref="ApiResult"/> 信封响应:
/// 连接失败/目标不可用返回 503,转发超时返回 504。
/// </summary>
public sealed class GatewayProxyErrorMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;

    /// <summary>创建中间件。</summary>
    public GatewayProxyErrorMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <inheritdoc/>
    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        var errorFeature = context.Features.Get<IForwarderErrorFeature>();
        if (errorFeature is null || context.Response.HasStarted)
        {
            return;
        }

        var (statusCode, message) = errorFeature.Error switch
        {
            ForwarderError.RequestTimedOut
                or ForwarderError.RequestCanceled
                or ForwarderError.RequestBodyCanceled
                or ForwarderError.ResponseBodyCanceled
                => (StatusCodes.Status504GatewayTimeout, "网关转发请求超时"),
            _ => (StatusCodes.Status503ServiceUnavailable, "下游服务不可用"),
        };

        var envelope = ApiResult.Fail<object?>(statusCode.ToString(CultureInfo.InvariantCulture), message);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(JsonSerializer.Serialize(envelope, JsonOptions), context.RequestAborted);
    }
}
