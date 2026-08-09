using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.Web.Middleware;

/// <summary>
/// 请求日志中间件:记录每个 HTTP 请求的方法、路径、状态码与耗时。
/// </summary>
public sealed partial class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        var path = context.Request.Path + context.Request.QueryString;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            LogRequestCompleted(method, path, context.Response.StatusCode, stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "HTTP {Method} {Path} 响应 {StatusCode} 耗时 {ElapsedMs} ms")]
    private partial void LogRequestCompleted(string method, string path, int statusCode, double elapsedMs);
}
