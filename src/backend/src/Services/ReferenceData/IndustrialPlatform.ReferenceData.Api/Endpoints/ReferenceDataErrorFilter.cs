using System.Diagnostics;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Infrastructure.Outbox;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.Web.Results;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.ReferenceData.Api.Endpoints;

public sealed partial class ReferenceDataErrorFilter(ILogger<ReferenceDataErrorFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await next(context);
            Log(context.HttpContext, "Success", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return result;
        }
        catch (ReferenceDataException error)
        {
            Log(context.HttpContext, error.ErrorCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return Failure(context.HttpContext, error.Status, error.ErrorCode, error.Field);
        }
        catch (BusinessException)
        {
            Log(context.HttpContext, "REF-INVALID-STATE", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return Failure(context.HttpContext, 409, "REF-INVALID-STATE", null);
        }
        catch (Exception exception)
        {
            Log(context.HttpContext, "Unhandled", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            LogUnhandled(logger, exception, Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
            throw;
        }
    }

    private void Log(HttpContext http, string result, double durationMs)
    {
        var actor = http.Items.TryGetValue(typeof(ReferenceDataActor), out var value)
            ? value as ReferenceDataActor
            : null;
        var path = http.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        var moduleIndex = Array.IndexOf(path, "admin") + 1;
        if (moduleIndex == 0) moduleIndex = Array.IndexOf(path, "reference-data") + 1;
        var module = moduleIndex > 0 && moduleIndex < path.Length ? path[moduleIndex] : "unknown";
        var objectNId = http.Request.RouteValues.GetValueOrDefault("nId")?.ToString()
            ?? http.Request.RouteValues.GetValueOrDefault("id")?.ToString();
        var revision = http.Request.RouteValues.GetValueOrDefault("revision")?.ToString();
        ReferenceDataMetrics.RecordApi(module, result, durationMs);
        if (module == "dynamic-configurations"
            && result is not "Success"
            && (result.StartsWith("REF-DYNAMIC", StringComparison.Ordinal)
                || result == "REF-VALIDATION-FAILED"))
            ReferenceDataMetrics.RecordDynamicValidationFailure();
        if (!logger.IsEnabled(LogLevel.Information)) return;
        var operation = string.Concat(http.Request.Method, " ", http.GetEndpoint()?.DisplayName);
        LogRequest(logger, "ReferenceData", Activity.Current?.Id ?? http.TraceIdentifier,
            actor?.TenantNId, actor?.UserNId, module, operation,
            objectNId, revision, durationMs, result);
    }

    private static IResult Failure(HttpContext http, int status, string code, string? field)
    {
        var result = ApiResult.Fail(code, code);
        result.TraceId = Activity.Current?.Id ?? http.TraceIdentifier;
        result.Parameters = field is null ? null : new { field };
        return Results.Json(result, statusCode: status);
    }

    [LoggerMessage(EventId = 3401, Level = LogLevel.Information,
        Message = "{ServiceName} request completed TraceId={TraceId} TenantNId={TenantNId} UserNId={UserNId} Module={Module} Operation={Operation} ObjectNId={ObjectNId} Revision={Revision} DurationMs={DurationMs} Result={Result}")]
    private static partial void LogRequest(ILogger logger, string serviceName, string traceId,
        string? tenantNId, string? userNId, string module, string operation, string? objectNId,
        string? revision, double durationMs, string result);

    [LoggerMessage(EventId = 3402, Level = LogLevel.Error,
        Message = "ReferenceData request failed unexpectedly TraceId={TraceId}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string traceId);
}

public static class ReferenceDataRequestErrors
{
    public static IApplicationBuilder UseReferenceDataRequestErrors(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        try { await next(http); }
        catch (BadHttpRequestException error) when (http.Request.Path.StartsWithSegments("/api/v1/reference-data"))
        {
            var result = ApiResult.Fail("REF-VALIDATION-FAILED", "REF-VALIDATION-FAILED");
            result.TraceId = Activity.Current?.Id ?? http.TraceIdentifier;
            http.Response.StatusCode = error.StatusCode;
            await http.Response.WriteAsJsonAsync(result, http.RequestAborted);
        }
    });
}
