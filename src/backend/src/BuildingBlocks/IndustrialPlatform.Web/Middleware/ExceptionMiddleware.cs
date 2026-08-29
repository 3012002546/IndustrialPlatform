using System.Text.Json;
using System.Diagnostics;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.Web.Errors;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.Web.Middleware;

/// <summary>
/// 全局异常处理中间件:将领域异常与未预期异常统一转换为 <see cref="ApiResult"/> 响应。
/// </summary>
public sealed partial class ExceptionMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (context.Response.HasStarted)
            {
                LogUnhandledException(context.Request.Method, context.Request.Path, exception);
                throw;
            }

            await HandleExceptionAsync(context, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, code) = MapStatusCode(exception);
        var message = exception is DomainException ? exception.Message : "Internal Server Error";

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        if (exception is DomainException domainException)
        {
            LogDomainException(domainException.GetType().Name, context.Request.Method, context.Request.Path, domainException);
        }
        else
        {
            LogUnhandledException(context.Request.Method, context.Request.Path, exception);
        }

        var descriptor = new ApiErrorDescriptor(
            code,
            message,
            TraceId: Activity.Current?.TraceId.ToString());
        var envelope = ApiResult.Fail<object?>(descriptor.Code, descriptor.Message);
        envelope.Parameters = descriptor.Parameters;
        envelope.TraceId = descriptor.TraceId;
        await JsonSerializer.SerializeAsync(context.Response.Body, envelope, JsonOptions, context.RequestAborted);
    }

    private static (int StatusCode, string Code) MapStatusCode(Exception exception)
        => exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "400"),
            BusinessException => (StatusCodes.Status400BadRequest, "400"),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, "401"),
            NotFoundException => (StatusCodes.Status404NotFound, "404"),
            _ => (StatusCodes.Status500InternalServerError, "500"),
        };

    [LoggerMessage(EventId = 101, Level = LogLevel.Error, Message = "未预期的异常发生在 HTTP {Method} {Path}")]
    private partial void LogUnhandledException(string method, string path, Exception exception);

    [LoggerMessage(EventId = 102, Level = LogLevel.Warning, Message = "领域异常 {ExceptionType} 发生在 HTTP {Method} {Path}")]
    private partial void LogDomainException(string exceptionType, string method, string path, Exception exception);
}
